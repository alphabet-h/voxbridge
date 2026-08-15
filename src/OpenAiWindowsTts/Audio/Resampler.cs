namespace OpenAiWindowsTts.Audio;

/// <summary>
/// 16,000 Hz を 48,000 Hz へ上げる。比がちょうど 3 倍の整数比なので**ポリフェーズ FIR** が使える。
///
/// <b>線形補間を使わないこと。</b> 3 倍のゼロ挿入で 15 kHz と 17 kHz（1 kHz の像）あたりに
/// イメージが立ち、線形補間の減衰では取り切れずに金属的な折り返し音が残る。
/// 「なんとなくシャリつく」という、指摘されるまで気づきにくい劣化になる。
///
/// 導出:
///   ゼロ挿入した u[i]（i % 3 != 0 なら 0）に長さ T の FIR を掛けると
///     y[3n+p] = Σ_k h[3k+p] · x[n-k]
///   になる。つまり位相 p は係数を 1 つおきに 3 つ飛ばしで使い、入力はそのまま辿ればよい。
///   ゼロを掛ける計算をしないので、素朴な実装の 1/3 の演算量で済む。
/// </summary>
public static class Resampler
{
    /// <summary>全体のタップ数。3 の倍数かつ奇数にして、群遅延を整数サンプルにしてある。</summary>
    private const int TapCount = 51;

    private const int TapsPerPhase = TapCount / CanonicalFormat.UpsampleFactor;

    /// <summary>プロトタイプの中心。<see cref="TapCount"/> が奇数なので整数になる。</summary>
    private const int GroupDelay = (TapCount - 1) / 2;

    /// <summary>遮断周波数（Hz）。元の Nyquist 8 kHz の少し下に置く。</summary>
    private const double CutoffHz = 7_600.0;

    /// <summary>位相ごとの係数。<c>Phases[p][k] = h[3k + p]</c>。</summary>
    private static readonly double[][] Phases = BuildPhases();

    /// <summary>16 kHz のモノラル PCM を 48 kHz へ。長さはちょうど 3 倍になる。</summary>
    public static short[] Upsample3x(ReadOnlySpan<short> input)
    {
        var output = new short[input.Length * CanonicalFormat.UpsampleFactor];

        for (var outputIndex = 0; outputIndex < output.Length; outputIndex++)
        {
            // 群遅延の分だけ先を読む。こうすると出力の先頭が入力の先頭と時間的に揃う
            var delayed = outputIndex + GroupDelay;
            var coefficients = Phases[delayed % CanonicalFormat.UpsampleFactor];
            var inputIndex = delayed / CanonicalFormat.UpsampleFactor;

            var sum = 0.0;
            for (var k = 0; k < TapsPerPhase; k++)
            {
                var source = inputIndex - k;
                if (source < 0)
                {
                    // これ以上さかのぼっても 0 しか無い
                    break;
                }

                if (source < input.Length)
                {
                    sum += coefficients[k] * input[source];
                }
            }

            output[outputIndex] = Saturate(sum);
        }

        return output;
    }

    private static short Saturate(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return (short)Math.Clamp(rounded, short.MinValue, short.MaxValue);
    }

    private static double[][] BuildPhases()
    {
        var prototype = BuildPrototype();

        var phases = new double[CanonicalFormat.UpsampleFactor][];
        for (var p = 0; p < CanonicalFormat.UpsampleFactor; p++)
        {
            var coefficients = new double[TapsPerPhase];
            for (var k = 0; k < TapsPerPhase; k++)
            {
                coefficients[k] = prototype[(k * CanonicalFormat.UpsampleFactor) + p];
            }

            // 位相ごとに合計 1 へ正規化する。
            // 全体で合わせるだけだと位相ごとに直流利得が僅かにずれ、
            // 直流や低い音に 16 kHz の変調が乗る（無音のはずの箇所が鳴く）。
            var total = coefficients.Sum();
            for (var k = 0; k < TapsPerPhase; k++)
            {
                coefficients[k] /= total;
            }

            phases[p] = coefficients;
        }

        return phases;
    }

    /// <summary>窓関数を掛けた sinc。Blackman-Harris は阻止域の漏れが -90 dB 台まで落ちる。</summary>
    private static double[] BuildPrototype()
    {
        const double a0 = 0.35875;
        const double a1 = 0.48829;
        const double a2 = 0.14128;
        const double a3 = 0.01168;

        var normalizedCutoff = CutoffHz / CanonicalFormat.SampleRate;
        var prototype = new double[TapCount];

        for (var j = 0; j < TapCount; j++)
        {
            var offset = j - GroupDelay;
            var sinc = offset == 0
                ? 2.0 * normalizedCutoff
                : Math.Sin(2.0 * Math.PI * normalizedCutoff * offset) / (Math.PI * offset);

            var phase = 2.0 * Math.PI * j / (TapCount - 1);
            var window = a0
                - (a1 * Math.Cos(phase))
                + (a2 * Math.Cos(2.0 * phase))
                - (a3 * Math.Cos(3.0 * phase));

            prototype[j] = sinc * window;
        }

        return prototype;
    }
}
