using System.Runtime.InteropServices;
using System.Security.Cryptography;
using VoxBridge.Audio;

namespace VoxBridge.Tests;

/// <summary>
/// リサンプラは「数値が合っている」だけでは検証にならない。**信号を通して測る。**
/// ここが緩いと、耳で聞いて初めて分かる金属的な折り返し音を持ち込むことになる。
/// </summary>
public class ResamplerTests(Xunit.Abstractions.ITestOutputHelper testOutput)
{
    private const int SourceRate = CanonicalFormat.SourceSampleRate;
    private const int TargetRate = CanonicalFormat.SampleRate;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(160)]
    [InlineData(16_000)]
    public void 長さはちょうど_3_倍になる(int length)
    {
        var output = Resampler.Upsample3x(new short[length]);

        Assert.Equal(length * 3, output.Length);
    }

    [Fact]
    public void 無音は無音のまま()
    {
        var output = Resampler.Upsample3x(new short[1_000]);

        Assert.All(output, sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void 直流が保たれる()
    {
        // 位相ごとに直流利得が揃っていないと、直流に 16 kHz の変調が乗って
        // 「無音のはずの箇所が鳴く」。フィルタの端は立ち上がるので中央だけ見る
        const short level = 8_000;
        var input = new short[1_000];
        Array.Fill(input, level);

        var output = Resampler.Upsample3x(input);

        foreach (var sample in output.AsSpan(300, 2_400).ToArray())
        {
            Assert.InRange(sample, level - 1, level + 1);
        }
    }

    [Fact]
    public void 正弦波の振幅が保たれる()
    {
        var input = Sine(1_000.0, SourceRate, SourceRate, amplitude: 10_000);

        var output = Resampler.Upsample3x(input);

        var peak = output.AsSpan(1_000, TargetRate - 2_000).ToArray().Max(Math.Abs);
        Assert.InRange(peak, 9_500, 10_500);
    }

    [Theory]
    [InlineData(15_000.0)] // 1 kHz の像（16 kHz - 1 kHz）
    [InlineData(17_000.0)] // 1 kHz の像（16 kHz + 1 kHz）
    [InlineData(31_000.0)] // 32 kHz - 1 kHz
    [InlineData(33_000.0)] // 32 kHz + 1 kHz
    public void ゼロ挿入で立つイメージが_60dB_以上落ちている(double imageHz)
    {
        // 線形補間だとここが -20 dB 程度しか落ちず、金属的な折り返し音として残る
        var input = Sine(1_000.0, SourceRate, SourceRate, amplitude: 10_000);
        var output = Resampler.Upsample3x(input);

        // フィルタの立ち上がり・立ち下がりを避けて中央を測る
        var middle = output.AsSpan(2_000, 32_768).ToArray();

        var signal = Magnitude(middle, 1_000.0, TargetRate);
        var image = Magnitude(middle, imageHz, TargetRate);

        var decibels = 20.0 * Math.Log10(image / signal);

        // 余裕がどれだけあるかを残す。フィルタを触ったときにここを見る
        testOutput.WriteLine($"{imageHz,8:F0} Hz のイメージ: {decibels,7:F1} dB（閾値 -60.0 dB）");

        Assert.True(decibels < -60.0, $"{imageHz} Hz のイメージが {decibels:F1} dB しか落ちていません");
    }

    [Fact]
    public void 元の帯域内の音は通す()
    {
        var input = Sine(3_000.0, SourceRate, SourceRate, amplitude: 10_000);
        var output = Resampler.Upsample3x(input);

        var middle = output.AsSpan(2_000, 32_768).ToArray();
        var signal = Magnitude(middle, 3_000.0, TargetRate);
        var reference = Magnitude(Sine(3_000.0, TargetRate, 32_768, amplitude: 10_000), 3_000.0, TargetRate);

        var decibels = 20.0 * Math.Log10(signal / reference);
        Assert.InRange(decibels, -1.0, 1.0);
    }

    /// <summary>
    /// 出力そのものを固定する。**リサンプラを速くするときの命綱。**
    ///
    /// 「速くなったが音が変わった」は、-103 dB のイメージ抑圧が保たれていても起こりうる
    /// （係数の並べ替えを間違える、境界の扱いを落とす、など）。
    /// 実装をいじったらこのハッシュが変わるので、意図しない変化に必ず気づける。
    ///
    /// **ハッシュが変わる変更をするときは、まず「変えていい」と判断すること。**
    /// 判断したうえで新しい値を焼き直し、-103 dB のテストと実際の音も確かめる。
    /// </summary>
    [Fact]
    public void 出力がビット単位で変わっていない()
    {
        var input = PseudoRandom(50_000, seed: 20260815);

        var output = Resampler.Upsample3x(input);
        var digest = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes<short>(output)));

        testOutput.WriteLine($"出力の SHA-256: {digest}");

        Assert.Equal("84A5A4C868596DEE6D86AA40E3F2447BDE1E1DBFACF7F0507C17D4B4F25BF977", digest);
    }

    /// <summary>線形合同法。実装をまたいで同じ入力を作るためだけのもの。</summary>
    private static short[] PseudoRandom(int count, int seed)
    {
        var state = (uint)seed;
        var samples = new short[count];

        for (var i = 0; i < count; i++)
        {
            state = (state * 1664525u) + 1013904223u;
            samples[i] = (short)((state >> 16) - 32768);
        }

        return samples;
    }

    [Fact]
    public void 振り切れた入力でも飽和して壊れない()
    {
        var input = new short[1_000];
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = i % 2 == 0 ? short.MaxValue : short.MinValue;
        }

        var output = Resampler.Upsample3x(input);

        Assert.All(output, sample => Assert.InRange(sample, short.MinValue, short.MaxValue));
    }

    private static short[] Sine(double frequencyHz, int sampleRate, int sampleCount, double amplitude)
    {
        var samples = new short[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            samples[i] = (short)Math.Round(amplitude * Math.Sin(2.0 * Math.PI * frequencyHz * i / sampleRate));
        }

        return samples;
    }

    /// <summary>
    /// Goertzel で 1 周波数だけ測る。Hann 窓を掛けるのは、
    /// 強い 1 kHz からの漏れが -90 dB の阻止域を覆い隠さないようにするため。
    /// </summary>
    private static double Magnitude(short[] signal, double frequencyHz, int sampleRate)
    {
        var omega = 2.0 * Math.PI * frequencyHz / sampleRate;
        var coefficient = 2.0 * Math.Cos(omega);

        var previous = 0.0;
        var beforePrevious = 0.0;

        for (var i = 0; i < signal.Length; i++)
        {
            var window = 0.5 - (0.5 * Math.Cos(2.0 * Math.PI * i / (signal.Length - 1)));
            var current = (signal[i] * window) + (coefficient * previous) - beforePrevious;
            beforePrevious = previous;
            previous = current;
        }

        var real = previous - (beforePrevious * Math.Cos(omega));
        var imaginary = beforePrevious * Math.Sin(omega);

        return Math.Sqrt((real * real) + (imaginary * imaginary)) / signal.Length;
    }
}
