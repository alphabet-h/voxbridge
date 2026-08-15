using System.Buffers.Binary;
using System.Text;
using OpenAiWindowsTts.Audio;

namespace OpenAiWindowsTts.Tests;

public class WavWriterTests
{
    [Fact]
    public void ヘッダは_44_バイトの標準形で_正規形を申告する()
    {
        var header = WavWriter.CreateHeader(1000);

        Assert.Equal(44, header.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(header, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(header, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(header, 12, 4));
        Assert.Equal("data", Encoding.ASCII.GetString(header, 36, 4));

        // 読むときは fmt 18 も受けるが、書くのは常に 16（docs/02 §5.3）
        Assert.Equal(16u, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16, 4)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(20, 2)));
        Assert.Equal(CanonicalFormat.Channels, BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(22, 2)));
        Assert.Equal((uint)CanonicalFormat.SampleRate, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(24, 4)));
        Assert.Equal((uint)CanonicalFormat.ByteRate, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(28, 4)));
        Assert.Equal(CanonicalFormat.BlockAlign, BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(32, 2)));
        Assert.Equal(CanonicalFormat.BitsPerSample, BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(34, 2)));
    }

    [Fact]
    public void 長さが確定しているときの_RIFF_と_data_の_size()
    {
        var header = WavWriter.CreateHeader(1000);

        Assert.Equal(1036u, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4)));
        Assert.Equal(1000u, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(40, 4)));
    }

    [Fact]
    public void ストリーミング用のヘッダは_size_を_0_にする()
    {
        // 合成が終わる前にヘッダを返すため。パーサは 0 を「残り全部」として読む
        var header = WavWriter.CreateHeader(WavWriter.UnknownLength);

        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(40, 4)));
    }

    [Fact]
    public void 自分で書いた_WAV_を自分で読み返せる()
    {
        var samples = new short[] { 0, 1, -1, 32767, -32768, 12345 };

        var wav = WavWriter.Create(samples);
        var content = WavReader.Read(wav);

        Assert.Equal(CanonicalFormat.SampleRate, content.Format.SampleRate);
        Assert.Equal(CanonicalFormat.Channels, content.Format.Channels);
        Assert.Equal(CanonicalFormat.BitsPerSample, content.Format.BitsPerSample);
        Assert.Equal(WavWriter.HeaderLength, content.DataOffset);
        Assert.Equal(samples.Length * 2, content.DataLength);

        var data = wav.AsSpan(content.DataOffset, content.DataLength);
        for (var i = 0; i < samples.Length; i++)
        {
            Assert.Equal(samples[i], BinaryPrimitives.ReadInt16LittleEndian(data.Slice(i * 2, 2)));
        }
    }

    [Fact]
    public void ストリーミングで書いた_WAV_も読み返せる()
    {
        // 実際の経路: ヘッダ（size 0）を先に流し、あとから PCM を継ぎ足す
        var samples = new short[] { 5, 6, 7, 8 };

        var wav = WavWriter.CreateHeader(WavWriter.UnknownLength)
            .Concat(WavWriter.ToPcmBytes(samples))
            .ToArray();

        var content = WavReader.Read(wav);

        Assert.Equal(samples.Length * 2, content.DataLength);
    }

    [Fact]
    public void 空の_PCM_でもヘッダだけの_WAV_になる()
    {
        var wav = WavWriter.Create([]);

        Assert.Equal(WavWriter.HeaderLength, wav.Length);
    }

    [Fact]
    public void 秒数は_data_の長さと一致する()
    {
        var oneSecond = new short[CanonicalFormat.SampleRate];

        var wav = WavWriter.Create(oneSecond);
        var content = WavReader.Read(wav);

        Assert.Equal(1.0, CanonicalFormat.DurationSeconds(content.DataLength), precision: 6);
    }

    [Fact]
    public void 領域が足りなければ書かずに投げる()
    {
        Assert.Throws<ArgumentException>(() => WavWriter.WriteHeader(new byte[43], 0));
        Assert.Throws<ArgumentException>(() => WavWriter.WritePcm(new byte[3], [1, 2]));
    }
}
