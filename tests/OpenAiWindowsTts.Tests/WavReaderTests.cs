using OpenAiWindowsTts.Audio;

namespace OpenAiWindowsTts.Tests;

public class WavReaderTests
{
    [Theory]
    [InlineData(16)] // 標準
    [InlineData(18)] // cbSize 付き。Windows の音声合成はこれ（ヘッダ 46 バイト）
    [InlineData(40)] // WAVE_FORMAT_EXTENSIBLE
    public void fmt_チャンク長が_16_でなくても読める(int formatChunkLength)
    {
        var wav = new WavBuilder
        {
            FormatChunkLength = formatChunkLength,
            Data = WavBuilder.Pcm16(100, -100, 200, -200),
        }.Build();

        var content = WavReader.Read(wav);

        Assert.Equal(16_000, content.Format.SampleRate);
        Assert.Equal(1, content.Format.Channels);
        Assert.Equal(16, content.Format.BitsPerSample);
        Assert.Equal(8, content.DataLength);
    }

    [Fact]
    public void Windows_の音声合成が返す_46_バイトヘッダで_data_が_offset_46_から始まる()
    {
        // 44 バイト決め打ちで切ると 2 バイトずれ、半サンプルずれた雑音になる。
        // 数値は正しく見えるので耳で聞くまで気づかない
        var wav = new WavBuilder { FormatChunkLength = 18 }.Build();

        var content = WavReader.Read(wav);

        Assert.Equal(46, content.DataOffset);
    }

    [Fact]
    public void fmt_と_data_の間に_LIST_を挟んでも読める()
    {
        var wav = new WavBuilder
        {
            ListChunk = "INFOISFT test"u8.ToArray(),
            Data = WavBuilder.Pcm16(1, 2, 3),
        }.Build();

        var content = WavReader.Read(wav);

        Assert.Equal(6, content.DataLength);
        Assert.Equal(16_000, content.Format.SampleRate);
    }

    [Fact]
    public void 奇数サイズのチャンクのパディングを飛ばせる()
    {
        var wav = new WavBuilder
        {
            ListChunk = [0x01, 0x02, 0x03], // 奇数 → 1 バイトのパディングが続く
            Data = WavBuilder.Pcm16(7, 8),
        }.Build();

        var content = WavReader.Read(wav);

        Assert.Equal(4, content.DataLength);
    }

    [Fact]
    public void data_の_size_が_0_なら残り全部として読む()
    {
        // ストリーミング書き出しの WAV は長さが確定する前に書き始めるので、この形になる
        var wav = new WavBuilder
        {
            Data = WavBuilder.Pcm16(1, 2, 3, 4, 5),
            DeclaredDataSize = 0,
        }.Build();

        var content = WavReader.Read(wav);

        Assert.Equal(10, content.DataLength);
    }

    [Fact]
    public void data_の_size_が_実バイト数を超えていても救済する()
    {
        var wav = new WavBuilder
        {
            Data = WavBuilder.Pcm16(1, 2),
            DeclaredDataSize = 0xFFFF_FFFF,
        }.Build();

        var content = WavReader.Read(wav);

        Assert.Equal(4, content.DataLength);
    }

    [Fact]
    public void PCM_の中身を元のバッファから切り出せる()
    {
        var samples = new short[] { 1000, -1000, 32767, -32768 };
        var wav = new WavBuilder { FormatChunkLength = 18, Data = WavBuilder.Pcm16(samples) }.Build();

        var content = WavReader.Read(wav);
        var data = wav.AsSpan(content.DataOffset, content.DataLength);

        for (var i = 0; i < samples.Length; i++)
        {
            Assert.Equal(samples[i], BitConverter.ToInt16(data.Slice(i * 2, 2)));
        }
    }

    [Fact]
    public void RF64_は明示的に拒否する()
    {
        var wav = new WavBuilder { RiffTag = "RF64" }.Build();

        var error = Assert.Throws<WavParseException>(() => WavReader.Read(wav));
        Assert.Contains("RF64", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RIFF_でなければ拒否する()
    {
        var wav = new WavBuilder { RiffTag = "JUNK" }.Build();

        Assert.Throws<WavParseException>(() => WavReader.Read(wav));
    }

    [Fact]
    public void 短すぎる入力を拒否する()
    {
        Assert.Throws<WavParseException>(() => WavReader.Read([0x52, 0x49, 0x46, 0x46]));
        Assert.Throws<WavParseException>(() => WavReader.Read([]));
    }

    [Fact]
    public void fmt_が無ければ拒否する()
    {
        var wav = new WavBuilder { OmitFormatChunk = true }.Build();

        var error = Assert.Throws<WavParseException>(() => WavReader.Read(wav));
        Assert.Contains("fmt", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void data_が無ければ拒否する()
    {
        var wav = new WavBuilder { OmitDataChunk = true }.Build();

        var error = Assert.Throws<WavParseException>(() => WavReader.Read(wav));
        Assert.Contains("data", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void fmt_の値が_0_なら拒否する()
    {
        var wav = new WavBuilder { Channels = 0 }.Build();

        Assert.Throws<WavParseException>(() => WavReader.Read(wav));
    }
}
