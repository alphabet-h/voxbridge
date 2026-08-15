using System.Runtime.InteropServices;
using OpenAiWindowsTts.Audio;
using Windows.Media.SpeechSynthesis;

namespace OpenAiWindowsTts.Speech;

/// <summary>Windows の音声合成が失敗した、または想定外のものを返した。</summary>
public sealed class SpeechSynthesisException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Windows の音声合成を呼んで **16 kHz の PCM** を取り出す。48 kHz へ上げるのは
/// <see cref="Resampler"/> の仕事で、ここではやらない（層を分けておくと、
/// 音がおかしいときに合成とリサンプルのどちらが原因か切り分けられる）。
/// </summary>
public sealed class SpeechEngine(VoiceCatalog catalog)
{
    /// <summary>
    /// <b>合成はプロセス内で 1 本ずつしか走らせられない。設定で変えられるようにしないこと。</b>
    ///
    /// OneCore の合成エンジン（<c>MSTTSEngine_OneCore.dll</c>）を同時に叩くと、
    /// 例外ではなく <c>__fastfail</c>（<c>0xc0000409</c> / STACK_BUFFER_OVERRUN）で
    /// **プロセスごと落ちる**。例外ではないので try/catch にもログにも一切かからず、
    /// 走っていた全リクエストが接続断になる。
    ///
    /// 実測（2026-08-15・Windows 11 build 26200）:
    ///   - リクエストごとに new/Dispose して 2 並列 → 25 回・1.1 秒で死亡
    ///   - インスタンスを使い回して 2 並列       → 約 180 回・3.3 秒で死亡
    ///   - この semaphore で直列化               → 430 回・25 秒 完走
    ///
    /// つまり壊しているのは生成・破棄の競合ではなく <b>合成呼び出しそのものの同時実行</b>。
    /// インスタンスを分けても避けられないので、ここで塞ぐしかない。
    /// 死ぬ手前でも、並列時は同じ入力から違うバイト列が返る（実測 61 件中 18 件）。
    /// </summary>
    private static readonly SemaphoreSlim SynthesisLock = new(1, 1);

    /// <summary>
    /// <c>SpeakingRate</c> の下限。**実測値**（ドキュメントは 0.5 と書いているが、
    /// 0.2 まで連続に効く。長さが 1/rate に比例し、有声区間の割合も変わらない）。
    ///
    /// ここを 0.5 にすると、契約上の有効値である <c>speed: 0.25</c> が
    /// <c>0.5</c> とバイト単位で同一の WAV を返す。**クランプがあるせいで
    /// 「設定できたのに効かない」が起きる。**
    /// </summary>
    public const double MinSpeakingRate = 0.2;

    /// <summary>
    /// <c>SpeakingRate</c> の上限。**ここは実測でも丸められる**（6.5 も 8.0 も 6.0 と同じ音）。
    /// 契約が <c>speed</c> を 4.0 で切っているので通常は到達しない。
    /// </summary>
    public const double MaxSpeakingRate = 6.0;

    public static double ClampSpeakingRate(double rate) => Math.Clamp(rate, MinSpeakingRate, MaxSpeakingRate);

    public async Task<short[]> SynthesizeAsync(
        string text,
        InstalledVoice voice,
        double speakingRate,
        CancellationToken cancellationToken)
    {
        var systemVoice = catalog.SystemVoice(voice);
        var wav = await SynthesizeToWavAsync(text, systemVoice, speakingRate, voice.Id, cancellationToken)
            .ConfigureAwait(false);

        return ExtractPcm(wav, voice);
    }

    /// <summary>
    /// 起動時に 1 回だけ捨て合成をする。
    ///
    /// プロセス最初の 1 回だけ、同じ入力・同じ声でも 2 回目以降と違う音が返る
    /// （長さは同じなので自動テストでは捕まらない）。ここで捨てておくと、
    /// 利用者の最初のリクエストがその 1 回目にならずに済む。
    /// </summary>
    public async Task WarmUpAsync(InstalledVoice voice, CancellationToken cancellationToken)
    {
        await SynthesizeAsync("あ", voice, 1.0, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> SynthesizeToWavAsync(
        string text,
        VoiceInformation systemVoice,
        double speakingRate,
        string voiceId,
        CancellationToken cancellationToken)
    {
        await SynthesisLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // リクエストごとに作って捨てる。使い回しは「遅い」と分かってからにする（docs/08 §3）
            using var synthesizer = new SpeechSynthesizer { Voice = systemVoice };
            synthesizer.Options.SpeakingRate = ClampSpeakingRate(speakingRate);

            using var synthesized = await synthesizer
                .SynthesizeTextToStreamAsync(text)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            using var source = synthesized.AsStreamForRead();
            using var buffer = new MemoryStream(checked((int)synthesized.Size));
            await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

            return buffer.ToArray();
        }
        catch (OperationCanceledException)
        {
            // 中断は失敗ではない。呼び出し側が握る
            throw;
        }
        catch (Exception exception) when (exception is not SpeechSynthesisException)
        {
            throw new SpeechSynthesisException(
                $"Windows の音声合成が失敗しました（voice={voiceId}）。", exception);
        }
        finally
        {
            SynthesisLock.Release();
        }
    }

    private static short[] ExtractPcm(ReadOnlySpan<byte> wav, InstalledVoice voice)
    {
        WavContent content;
        try
        {
            // チャンクを走査する。返ってくるのは fmt 長 18 の 46 バイトヘッダで、
            // 44 バイト決め打ちで切ると 2 バイトずれた雑音になる（docs/03 §3）
            content = WavReader.Read(wav);
        }
        catch (WavParseException exception)
        {
            throw new SpeechSynthesisException(
                $"Windows の音声合成が返した WAV を読めませんでした（voice={voice.Id}）。", exception);
        }

        var format = content.Format;
        var expected = format is
        {
            AudioFormat: WavFormat.PcmFormatTag,
            SampleRate: CanonicalFormat.SourceSampleRate,
            Channels: CanonicalFormat.SourceChannels,
            BitsPerSample: CanonicalFormat.SourceBitsPerSample,
        };

        if (!expected)
        {
            // 黙って別の形式を通すより落ちたほうがいい。
            // Windows 側の仕様が変わったことに、音を聞くまで気づかないのが最悪
            throw new SpeechSynthesisException(
                $"Windows の音声合成が想定外の形式を返しました" +
                $"（{format.SampleRate} Hz / {format.Channels}ch / {format.BitsPerSample}bit / tag={format.AudioFormat}）。" +
                $"想定は {CanonicalFormat.SourceSampleRate} Hz / {CanonicalFormat.SourceChannels}ch / " +
                $"{CanonicalFormat.SourceBitsPerSample}bit です。");
        }

        // WAV も Windows も little-endian なので、そのまま short として読める
        var data = wav.Slice(content.DataOffset, content.DataLength - (content.DataLength % 2));
        return MemoryMarshal.Cast<byte, short>(data).ToArray();
    }
}
