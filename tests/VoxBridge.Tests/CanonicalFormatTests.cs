using VoxBridge.Audio;

namespace VoxBridge.Tests;

public class CanonicalFormatTests
{
    [Fact]
    public void 正規形は_48kHz_モノラル_16bit()
    {
        Assert.Equal(48_000, CanonicalFormat.SampleRate);
        Assert.Equal(1, CanonicalFormat.Channels);
        Assert.Equal(16, CanonicalFormat.BitsPerSample);
    }

    [Fact]
    public void 変換比は整数の_3_倍()
    {
        // 整数比でなくなるとポリフェーズ FIR が使えなくなり、
        // リサンプラの設計をやり直すことになる（docs/02 §5）
        Assert.Equal(16_000, CanonicalFormat.SourceSampleRate);
        Assert.Equal(3, CanonicalFormat.UpsampleFactor);
        Assert.Equal(0, CanonicalFormat.SampleRate % CanonicalFormat.SourceSampleRate);
    }

    [Fact]
    public void 導出値は_WAV_ヘッダに書く値と一致する()
    {
        Assert.Equal(2, CanonicalFormat.BytesPerSample);
        Assert.Equal(2, CanonicalFormat.BlockAlign);
        Assert.Equal(96_000, CanonicalFormat.ByteRate);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(96_000, 1.0)]
    [InlineData(48_000, 0.5)]
    public void 長さは_PCM_のバイト数から出す(long pcmByteCount, double expectedSeconds)
    {
        Assert.Equal(expectedSeconds, CanonicalFormat.DurationSeconds(pcmByteCount), precision: 6);
    }
}
