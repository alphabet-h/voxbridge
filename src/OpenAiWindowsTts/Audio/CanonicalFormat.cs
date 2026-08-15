namespace OpenAiWindowsTts.Audio;

/// <summary>
/// このサーバが返す WAV の形。<b>48000 / 1 / 16 という数値をここ以外に書かないこと。</b>
///
/// Windows の音声合成が実際に吐くのは 16,000 Hz なので、返す前に必ず 3 倍へ上げる。
/// 変換を挟まず 16 kHz のまま返すと、サンプルレートを変換しないクライアントで
/// 「接続はできるのに、生成のたびに再試行不可のエラーになる」という壊れ方をする。
/// </summary>
public static class CanonicalFormat
{
    public const int SampleRate = 48_000;
    public const int Channels = 1;
    public const int BitsPerSample = 16;

    /// <summary>Windows の音声合成が吐く生の形式。ここから <see cref="SampleRate"/> へ上げる。</summary>
    public const int SourceSampleRate = 16_000;

    public const int SourceChannels = 1;

    public const int SourceBitsPerSample = 16;

    /// <summary>整数比なので、ポリフェーズ FIR が使える（<c>docs/02</c> §5）。</summary>
    public const int UpsampleFactor = SampleRate / SourceSampleRate;

    public const int BytesPerSample = BitsPerSample / 8;
    public const int BlockAlign = Channels * BytesPerSample;
    public const int ByteRate = SampleRate * BlockAlign;

    /// <summary>PCM のバイト数から秒数を出す。長さは常にこの式で実測し、申告値を信じない。</summary>
    public static double DurationSeconds(long pcmByteCount) =>
        (double)(pcmByteCount / BlockAlign) / SampleRate;
}
