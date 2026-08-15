using System.Buffers.Binary;
using System.Text;

namespace VoxBridge.Tests;

/// <summary>
/// テスト用の WAV を組み立てる。**わざと変わった形を作れる**ことが目的。
///
/// Windows の音声合成が返すのは fmt チャンク長 18 の 46 バイトヘッダなので、
/// 44 バイト決め打ちのパーサはそこで壊れる。その形を再現できないと検証にならない。
/// </summary>
internal sealed class WavBuilder
{
    private readonly List<byte> _bytes = [];

    public string RiffTag { get; init; } = "RIFF";

    /// <summary>16（標準）/ 18（cbSize 付き。Windows の音声合成はこれ）/ 40（EXTENSIBLE）。</summary>
    public int FormatChunkLength { get; init; } = 16;

    public int AudioFormat { get; init; } = 1;

    public int Channels { get; init; } = 1;

    public int SampleRate { get; init; } = 16_000;

    public int BitsPerSample { get; init; } = 16;

    public byte[] Data { get; init; } = [0x11, 0x22, 0x33, 0x44];

    /// <summary>data チャンクに書く size。null なら実際の長さ。0 でストリーミング書き出しを再現できる。</summary>
    public uint? DeclaredDataSize { get; init; }

    /// <summary>fmt と data の間に LIST チャンクを挟む。</summary>
    public byte[]? ListChunk { get; init; }

    public bool OmitFormatChunk { get; init; }

    public bool OmitDataChunk { get; init; }

    public byte[] Build()
    {
        _bytes.Clear();

        Ascii(RiffTag);
        UInt32(0); // あとで埋める
        Ascii("WAVE");

        if (!OmitFormatChunk)
        {
            Ascii("fmt ");
            UInt32((uint)FormatChunkLength);
            UInt16((ushort)AudioFormat);
            UInt16((ushort)Channels);
            UInt32((uint)SampleRate);
            UInt32((uint)(SampleRate * Channels * (BitsPerSample / 8)));
            UInt16((ushort)(Channels * (BitsPerSample / 8)));
            UInt16((ushort)BitsPerSample);

            // 16 バイトを超える分（cbSize や EXTENSIBLE の中身）は 0 で埋める
            for (var i = 16; i < FormatChunkLength; i++)
            {
                _bytes.Add(0);
            }

            if (FormatChunkLength % 2 != 0)
            {
                _bytes.Add(0);
            }
        }

        if (ListChunk is not null)
        {
            Ascii("LIST");
            UInt32((uint)ListChunk.Length);
            _bytes.AddRange(ListChunk);
            if (ListChunk.Length % 2 != 0)
            {
                _bytes.Add(0);
            }
        }

        if (!OmitDataChunk)
        {
            Ascii("data");
            UInt32(DeclaredDataSize ?? (uint)Data.Length);
            _bytes.AddRange(Data);
            if (Data.Length % 2 != 0)
            {
                _bytes.Add(0);
            }
        }

        var wav = _bytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4, 4), (uint)(wav.Length - 8));
        return wav;
    }

    /// <summary>16bit のサンプル列をそのままバイト列にする。</summary>
    public static byte[] Pcm16(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2, 2), samples[i]);
        }

        return bytes;
    }

    private void Ascii(string tag) => _bytes.AddRange(Encoding.ASCII.GetBytes(tag));

    private void UInt32(uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        _bytes.AddRange(buffer.ToArray());
    }

    private void UInt16(ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        _bytes.AddRange(buffer.ToArray());
    }
}
