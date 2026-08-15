using System.Buffers.Binary;
using System.Text;

namespace VoxBridge.Audio;

/// <summary>
/// 正規形（<see cref="CanonicalFormat"/>）の WAV を書く。
///
/// <b>読むときは緩く、書くときは厳しく。</b> 出すのは <c>fmt</c> チャンク長 16 の
/// 44 バイト標準ヘッダだけにする。
/// </summary>
public static class WavWriter
{
    /// <summary>標準ヘッダの長さ。RIFF(12) + fmt(24) + data ヘッダ(8)。</summary>
    public const int HeaderLength = 44;

    /// <summary>
    /// 長さが確定していないときにヘッダへ書くサイズ。
    ///
    /// 合成が終わる前にヘッダを返す（チャンク転送）ために使う。WAV のパーサは
    /// <c>data</c> の size が 0 のとき「残り全部」として読むのが通例で、
    /// ストリーミング書き出しの WAV は普通この形になる。
    /// </summary>
    public const int UnknownLength = 0;

    /// <summary>ヘッダを 1 つ作る。<paramref name="dataLength"/> に <see cref="UnknownLength"/> を渡すとストリーミング用。</summary>
    public static byte[] CreateHeader(int dataLength)
    {
        var header = new byte[HeaderLength];
        WriteHeader(header, dataLength);
        return header;
    }

    public static void WriteHeader(Span<byte> destination, int dataLength)
    {
        if (destination.Length < HeaderLength)
        {
            throw new ArgumentException($"ヘッダには {HeaderLength} バイト必要です。", nameof(destination));
        }

        if (dataLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dataLength), dataLength, "負の長さは書けません。");
        }

        // RIFF の size は「これ以降のバイト数」＝ ヘッダの残り 36 + data
        var riffSize = dataLength == UnknownLength ? 0u : (uint)(HeaderLength - 8 + dataLength);

        Ascii(destination[..4], "RIFF");
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), riffSize);
        Ascii(destination.Slice(8, 4), "WAVE");

        Ascii(destination.Slice(12, 4), "fmt ");
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(20, 2), WavFormat.PcmFormatTag);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(22, 2), CanonicalFormat.Channels);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(24, 4), CanonicalFormat.SampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(28, 4), CanonicalFormat.ByteRate);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(32, 2), CanonicalFormat.BlockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(34, 2), CanonicalFormat.BitsPerSample);

        Ascii(destination.Slice(36, 4), "data");
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(40, 4), (uint)dataLength);
    }

    /// <summary>PCM をリトルエンディアンの 16bit 列として書く。</summary>
    public static void WritePcm(Span<byte> destination, ReadOnlySpan<short> pcm)
    {
        if (destination.Length < pcm.Length * CanonicalFormat.BytesPerSample)
        {
            throw new ArgumentException("PCM を書くには領域が足りません。", nameof(destination));
        }

        for (var i = 0; i < pcm.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(
                destination.Slice(i * CanonicalFormat.BytesPerSample, CanonicalFormat.BytesPerSample),
                pcm[i]);
        }
    }

    public static byte[] ToPcmBytes(ReadOnlySpan<short> pcm)
    {
        var bytes = new byte[pcm.Length * CanonicalFormat.BytesPerSample];
        WritePcm(bytes, pcm);
        return bytes;
    }

    /// <summary>長さが確定している WAV を 1 本作る。</summary>
    public static byte[] Create(ReadOnlySpan<short> pcm)
    {
        var dataLength = pcm.Length * CanonicalFormat.BytesPerSample;
        var wav = new byte[HeaderLength + dataLength];

        WriteHeader(wav, dataLength);
        WritePcm(wav.AsSpan(HeaderLength), pcm);

        return wav;
    }

    private static void Ascii(Span<byte> destination, string tag) =>
        Encoding.ASCII.GetBytes(tag, destination);
}
