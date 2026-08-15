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

    /// <summary>
    /// 位相ごとの係数を 1 本に並べたもの。<c>FlatPhases[p * TapsPerPhase + k] = h[3k + p]</c>。
    ///
    /// ジャグ配列（<c>double[][]</c>）にすると出力サンプルごとに参照をもう 1 段たどる。
    /// 平たくしておくと <see cref="ReadOnlySpan{T}"/> で切り出せて、内側のループから
    /// バウンドチェックが落ちる。
    /// </summary>
    private static readonly double[] FlatPhases = BuildFlatPhases();

    /// <summary>
    /// 出力が始まる入力位置。<c>3n - GroupDelay</c> が 0 以上になる最初の <c>n</c>。
    /// </summary>
    private const int FirstInput = GroupDelay / CanonicalFormat.UpsampleFactor;

    /// <summary>
    /// 16 kHz のモノラル PCM を 48 kHz へ。長さはちょうど 3 倍になる。
    ///
    /// **入力サンプルで回して 3 出力をまとめて出す。**
    /// 出力ごとに回すと、同じ入力窓 17 個を 3 回読み直すことになる。
    /// 入力 <c>n</c> を共有する 3 出力は <c>3n - GroupDelay + p</c>（p = 0,1,2）。
    ///
    /// 窓が入力からはみ出すのは前後の端だけなので、内側では境界の判定を一切しない。
    /// </summary>
    public static short[] Upsample3x(ReadOnlySpan<short> input)
    {
        var length = input.Length;
        var output = new short[length * CanonicalFormat.UpsampleFactor];
        if (length == 0)
        {
            return output;
        }

        var coefficients = new ReadOnlySpan<double>(FlatPhases);
        var destination = output.AsSpan();

        // 窓 x[n-16..n] が丸ごと入力の中に収まるのは n が [TapsPerPhase-1, length-1] のとき。
        // そこだけ速い経路を通す
        var interiorStart = Math.Max(FirstInput, TapsPerPhase - 1);
        var interiorEnd = Math.Max(interiorStart, length);

        for (var n = FirstInput; n < interiorStart; n++)
        {
            EmitGuarded(input, coefficients, destination, n);
        }

        // 位相の切り出しはループの外へ。中で毎回 Slice すると入力サンプルの数だけ繰り返される
        var phase0 = coefficients.Slice(0, TapsPerPhase);
        var phase1 = coefficients.Slice(TapsPerPhase, TapsPerPhase);
        var phase2 = coefficients.Slice(2 * TapsPerPhase, TapsPerPhase);

        for (var n = interiorStart; n < interiorEnd; n++)
        {
            // window[j] = x[n - TapsPerPhase + 1 + j] なので、x[n-k] は window[TapsPerPhase-1-k]
            var window = input.Slice(n - TapsPerPhase + 1, TapsPerPhase);
            var baseIndex = (CanonicalFormat.UpsampleFactor * n) - GroupDelay;

            // **1 回読んだ入力サンプルを 3 つの位相で使い回す。** これが入力基準で回す理由。
            // 位相を外側にすると、同じ窓 17 個を 3 回読み直すことになる
            var sum0 = 0.0;
            var sum1 = 0.0;
            var sum2 = 0.0;

            for (var k = 0; k < TapsPerPhase; k++)
            {
                // k の昇順で足す。**順序を変えないこと** — 浮動小数の加算は結合しないので、
                // 逆順にすると最下位ビットが変わり、出力のハッシュが変わる
                double sample = window[TapsPerPhase - 1 - k];
                sum0 += phase0[k] * sample;
                sum1 += phase1[k] * sample;
                sum2 += phase2[k] * sample;
            }

            destination[baseIndex] = Saturate(sum0);
            destination[baseIndex + 1] = Saturate(sum1);
            destination[baseIndex + 2] = Saturate(sum2);
        }

        // 末尾。窓が右へはみ出しながら、群遅延の分だけ出力が続く
        for (var n = interiorEnd; n <= length + FirstInput; n++)
        {
            EmitGuarded(input, coefficients, destination, n);
        }

        return output;
    }

    /// <summary>窓や出力が範囲からはみ出す端。入力の外は 0 とみなす。</summary>
    private static void EmitGuarded(
        ReadOnlySpan<short> input,
        ReadOnlySpan<double> coefficients,
        Span<short> destination,
        int n)
    {
        var baseIndex = (CanonicalFormat.UpsampleFactor * n) - GroupDelay;

        for (var p = 0; p < CanonicalFormat.UpsampleFactor; p++)
        {
            var outputIndex = baseIndex + p;
            if (outputIndex < 0 || outputIndex >= destination.Length)
            {
                continue;
            }

            var phase = coefficients.Slice(p * TapsPerPhase, TapsPerPhase);

            var sum = 0.0;
            for (var k = 0; k < TapsPerPhase; k++)
            {
                var source = n - k;
                if (source < 0)
                {
                    // これ以上さかのぼっても 0 しか無い
                    break;
                }

                if (source < input.Length)
                {
                    sum += phase[k] * input[source];
                }
            }

            destination[outputIndex] = Saturate(sum);
        }
    }

    private static short Saturate(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return (short)Math.Clamp(rounded, short.MinValue, short.MaxValue);
    }

    private static double[] BuildFlatPhases()
    {
        var prototype = BuildPrototype();
        var flat = new double[CanonicalFormat.UpsampleFactor * TapsPerPhase];

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
                flat[(p * TapsPerPhase) + k] = coefficients[k] / total;
            }
        }

        return flat;
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
