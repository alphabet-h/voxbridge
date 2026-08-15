using VoxBridge.Audio;
using VoxBridge.Speech;

namespace VoxBridge.Tests;

/// <summary>
/// 実際に Windows の音声合成を叩くテスト。数秒かかるが、
/// <b>プロセスごと落ちるバグを止めているのはここだけ</b>なので消さないこと。
/// </summary>
public class SpeechEngineTests
{
    [Theory]
    [InlineData(0.1, SpeechEngine.MinSpeakingRate)]
    [InlineData(0.2, 0.2)]
    [InlineData(0.25, 0.25)] // 契約上の下限。ここが 0.5 に潰れると「設定できたのに効かない」になる
    [InlineData(1.0, 1.0)]
    [InlineData(4.0, 4.0)]
    [InlineData(9.0, SpeechEngine.MaxSpeakingRate)]
    public void 話速のクランプは実測した有効域に合わせる(double requested, double expected)
    {
        Assert.Equal(expected, SpeechEngine.ClampSpeakingRate(requested));
    }

    [Fact]
    public void 契約が受ける_speed_の全域がクランプされずに通る()
    {
        // 契約は 0.25〜4.0 を受ける（SpeechRequestValidator）。
        // Windows 側の有効域 0.2〜6.0 がそれを完全に含んでいるので、素通りするのが正しい
        Assert.True(SpeechEngine.MinSpeakingRate <= 0.25);
        Assert.True(SpeechEngine.MaxSpeakingRate >= 4.0);
    }

    [Fact]
    public async Task 並列に呼んでもプロセスが落ちない()
    {
        // OneCore の合成エンジン（MSTTSEngine_OneCore.dll）を同時に叩くと、
        // 例外ではなく __fastfail（0xc0000409）で**プロセスごと落ちる**。
        // このテストが失敗するときはテストホストごと消えるので、
        // 「アサーションが落ちた」ではなく「実行が消えた」形で表面化する。
        var catalog = VoiceCatalog.Load();
        var engine = new SpeechEngine(catalog);
        var voice = catalog.Default;
        Assert.NotNull(voice);

        const int rounds = 10;
        const int parallel = 4;

        for (var round = 0; round < rounds; round++)
        {
            var calls = Enumerable.Range(0, parallel)
                .Select(index => engine.SynthesizeAsync(
                    $"これは{index}番目の並列テストです。", voice, 1.0, CancellationToken.None))
                .ToArray();

            var results = await Task.WhenAll(calls);

            Assert.All(results, pcm => Assert.NotEmpty(pcm));
        }
    }

    [Fact]
    public async Task 同じ入力からは同じ音が返る()
    {
        // 並列に叩くと、死ぬ手前でも同じ入力から違うバイト列が返る。
        // 直列化が効いていることを、長さではなく中身で確かめる
        var catalog = VoiceCatalog.Load();
        var engine = new SpeechEngine(catalog);
        var voice = catalog.Default;
        Assert.NotNull(voice);

        // プロセス最初の 1 回だけ音が違うので、比較の前に捨てる
        await engine.WarmUpAsync(voice, CancellationToken.None);

        const string text = "同じ入力からは同じ音が返るはずです。";
        var first = await engine.SynthesizeAsync(text, voice, 1.0, CancellationToken.None);
        var second = await engine.SynthesizeAsync(text, voice, 1.0, CancellationToken.None);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task 返るのは_16kHz_の_PCM()
    {
        var catalog = VoiceCatalog.Load();
        var engine = new SpeechEngine(catalog);
        var voice = catalog.Default;
        Assert.NotNull(voice);

        var pcm = await engine.SynthesizeAsync("音声の形式を確かめます。", voice, 1.0, CancellationToken.None);

        Assert.NotEmpty(pcm);

        // 48 kHz へ上げるのは Audio/Resampler の仕事。ここは 16 kHz のまま返す
        var seconds = (double)pcm.Length / CanonicalFormat.SourceSampleRate;
        Assert.InRange(seconds, 0.5, 30.0);
    }

    [Fact]
    public async Task 話速を上げると短くなる()
    {
        var catalog = VoiceCatalog.Load();
        var engine = new SpeechEngine(catalog);
        var voice = catalog.Default;
        Assert.NotNull(voice);

        const string text = "話す速さを変えたときの長さを確かめます。";
        var slow = await engine.SynthesizeAsync(text, voice, 0.5, CancellationToken.None);
        var normal = await engine.SynthesizeAsync(text, voice, 1.0, CancellationToken.None);
        var fast = await engine.SynthesizeAsync(text, voice, 2.0, CancellationToken.None);

        Assert.True(slow.Length > normal.Length, $"0.5 が 1.0 より短い（{slow.Length} <= {normal.Length}）");
        Assert.True(normal.Length > fast.Length, $"1.0 が 2.0 より短い（{normal.Length} <= {fast.Length}）");
    }

    [Fact]
    public async Task 契約の下限_0_25_は_0_5_と別の音になる()
    {
        // 下限を 0.5 でクランプしていた頃は、この 2 つがバイト単位で同一だった。
        // 「設定できたのに効かない」を、クランプ側の都合で作らないこと
        var catalog = VoiceCatalog.Load();
        var engine = new SpeechEngine(catalog);
        var voice = catalog.Default;
        Assert.NotNull(voice);

        const string text = "遅い話速を確かめます。";
        var quarter = await engine.SynthesizeAsync(text, voice, 0.25, CancellationToken.None);
        var half = await engine.SynthesizeAsync(text, voice, 0.5, CancellationToken.None);

        Assert.True(quarter.Length > half.Length, $"0.25 が 0.5 より長い（{quarter.Length} <= {half.Length}）");
    }

    [Fact]
    public async Task 声を切り替えても_それぞれの声で返る()
    {
        // 合成器を声ごとに使い回すので、**前の声のまま合成する**バグが入りうる。
        // 長さでは捕まらない（同じ文なら声が違っても長さは近い）ので中身で見る
        var catalog = VoiceCatalog.Load();
        var engine = new SpeechEngine(catalog);

        if (catalog.Voices.Count < 2)
        {
            // 声が 1 つしか無い PC では確かめようがない
            return;
        }

        var first = catalog.Voices[0];
        var second = catalog.Voices[1];

        // プロセス最初の 1 回だけ音が違う（docs/03 §6）ので、比較の前に捨てる
        await engine.WarmUpAsync(first, CancellationToken.None);

        const string text = "声を切り替えて確かめます。";
        var firstVoice = await engine.SynthesizeAsync(text, first, 1.0, CancellationToken.None);
        var secondVoice = await engine.SynthesizeAsync(text, second, 1.0, CancellationToken.None);
        var firstAgain = await engine.SynthesizeAsync(text, first, 1.0, CancellationToken.None);

        Assert.NotEqual(firstVoice, secondVoice);

        // 戻したら最初と同じ音。合成器を切り替えても設定が持ち越されていないこと
        Assert.Equal(firstVoice, firstAgain);
    }

    [Fact]
    public async Task 中断のあとでも合成を続けられる()
    {
        // 合成器を使い回すので、**中断の巻き添えで以後のリクエストが壊れる**という
        // 壊れ方がありうる。中断を挟んだあとも合成できて、かつ安定することを見る。
        //
        // **中断の直後 1 回だけは音が変わる。** これは使い回しとは無関係で、
        // 毎回 new する実装でも同じように起きる（docs/03 §6.1 に実測）。
        // だから「直後の 1 回」ではなく「その次から安定するか」を検査する
        var catalog = VoiceCatalog.Load();
        var engine = new SpeechEngine(catalog);
        var voice = catalog.Default;
        Assert.NotNull(voice);

        await engine.WarmUpAsync(voice, CancellationToken.None);

        // 合成の途中で中断する。間に合わずに完走することもあるが、それでも問題はない
        var longText = string.Concat(Enumerable.Repeat("中断されるかもしれない長い文章です。", 20));
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(2));
            try
            {
                await engine.SynthesizeAsync(longText, voice, 1.0, cancelled.Token);
            }
            catch (OperationCanceledException)
            {
                // 中断できた。それが狙い
            }
        }

        const string text = "中断のあとでも合成を続けられるはずです。";

        // 直後の 1 回は揺れるので、揺れる枠として先に 1 回引く
        var first = await engine.SynthesizeAsync(text, voice, 1.0, CancellationToken.None);
        Assert.NotEmpty(first);

        var second = await engine.SynthesizeAsync(text, voice, 1.0, CancellationToken.None);
        var third = await engine.SynthesizeAsync(text, voice, 1.0, CancellationToken.None);

        Assert.Equal(second, third);
    }

    [Fact]
    public async Task 中断すると_OperationCanceledException_で返る()
    {
        var catalog = VoiceCatalog.Load();
        var engine = new SpeechEngine(catalog);
        var voice = catalog.Default;
        Assert.NotNull(voice);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.SynthesizeAsync("中断されるはずです。", voice, 1.0, cancelled.Token));
    }
}
