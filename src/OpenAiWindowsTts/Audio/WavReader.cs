using System.Buffers.Binary;
using System.Text;

namespace OpenAiWindowsTts.Audio;

/// <summary>WAV を読めなかった。合成の失敗として扱う（利用者向けの分類は呼び出し側で決める）。</summary>
public sealed class WavParseException(string message) : Exception(message);

/// <summary>WAV の書式。</summary>
public readonly record struct WavFormat(int AudioFormat, int Channels, int SampleRate, int BitsPerSample)
{
    public const int PcmFormatTag = 1;

    public int BlockAlign => Channels * (BitsPerSample / 8);
}

/// <summary>読み取り結果。PCM 本体は元のバッファの範囲として返す（コピーしない）。</summary>
public readonly record struct WavContent(WavFormat Format, int DataOffset, int DataLength);

/// <summary>
/// WAV のチャンクを走査して <c>fmt</c> と <c>data</c> を取り出す。
///
/// <b>44 バイト固定ヘッダを前提にしないこと。</b> Windows の音声合成が返す WAV は
/// <c>fmt</c> チャンク長 18（<c>cbSize = 0</c> 付き）の <b>46 バイトヘッダ</b>で、
/// 44 で切ると 2 バイトずれる。16bit なので半サンプルのずれになり、
/// バイトが入れ替わった雑音混じりの音が出る。<b>数値は正しく見えるので耳で聞くまで気づかない。</b>
/// </summary>
public static class WavReader
{
    private const int RiffHeaderLength = 12;
    private const int ChunkHeaderLength = 8;

    /// <summary>fmt チャンクの本体に最低限必要なバイト数（cbSize 抜きの WAVEFORMAT）。</summary>
    private const int MinimumFormatChunkLength = 16;

    public static WavContent Read(ReadOnlySpan<byte> wav)
    {
        if (wav.Length < RiffHeaderLength)
        {
            throw new WavParseException($"WAV が短すぎます（{wav.Length} バイト）。");
        }

        var riffTag = Ascii(wav[..4]);
        if (riffTag == "RF64")
        {
            throw new WavParseException("RF64 には対応していません。");
        }

        if (riffTag != "RIFF" || Ascii(wav.Slice(8, 4)) != "WAVE")
        {
            throw new WavParseException("RIFF/WAVE ではありません。");
        }

        WavFormat? format = null;
        var dataOffset = -1;
        var dataLength = 0;

        var offset = RiffHeaderLength;
        while (offset + ChunkHeaderLength <= wav.Length)
        {
            var id = Ascii(wav.Slice(offset, 4));
            var declared = BinaryPrimitives.ReadUInt32LittleEndian(wav.Slice(offset + 4, 4));
            var body = offset + ChunkHeaderLength;
            var available = wav.Length - body;

            // ストリーミング書き出しの WAV は size を 0（や実バイト数超過）のまま残すことがある。
            // 残り全部として読む — これは壊れた WAV ではなく、長さが確定する前に書き始めた WAV。
            var size = declared == 0 || declared > (uint)available ? available : (int)declared;

            if (id == "fmt " && size >= MinimumFormatChunkLength)
            {
                format = ReadFormat(wav.Slice(body, size));
            }
            else if (id == "data")
            {
                dataOffset = body;
                dataLength = size;
            }

            // チャンクは偶数境界。奇数サイズには 1 バイトのパディングが続く
            offset = body + size + (size % 2);
        }

        if (format is null)
        {
            throw new WavParseException("fmt チャンクがありません。");
        }

        if (dataOffset < 0)
        {
            throw new WavParseException("data チャンクがありません。");
        }

        return new WavContent(format.Value, dataOffset, dataLength);
    }

    private static WavFormat ReadFormat(ReadOnlySpan<byte> body)
    {
        var format = new WavFormat(
            AudioFormat: BinaryPrimitives.ReadUInt16LittleEndian(body[..2]),
            Channels: BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(2, 2)),
            SampleRate: (int)BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(4, 4)),
            BitsPerSample: BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(14, 2)));

        if (format.Channels <= 0 || format.SampleRate <= 0 || format.BitsPerSample <= 0)
        {
            throw new WavParseException(
                $"fmt の値が不正です（{format.SampleRate} Hz / {format.Channels}ch / {format.BitsPerSample}bit）。");
        }

        return format;
    }

    private static string Ascii(ReadOnlySpan<byte> bytes) => Encoding.ASCII.GetString(bytes);
}
