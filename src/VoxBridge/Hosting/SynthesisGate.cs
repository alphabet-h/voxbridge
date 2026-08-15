using VoxBridge.Contract;

namespace VoxBridge.Hosting;

/// <summary>
/// 同時に走る合成の本数を絞る。
///
/// Windows の音声合成は GPU を使わず CPU も軽いので技術的にはもっと出せるが、
/// 既定を 1 にしておくと <c>/health</c> の <c>max_concurrent_synthesis</c> を読むクライアントの
/// 挙動が GPU 前提のエンジンと揃い、驚きが少ない。
///
/// <b>クライアントが切断したら待ち行列から即座に外れること。</b> ここを怠ると、
/// 連打されたぶんだけ枠が埋まり、以降のリクエストが全部 503 になる。
/// </summary>
public sealed class SynthesisGate(int maxConcurrent, TimeSpan queueTimeout) : IDisposable
{
    private readonly SemaphoreSlim _slots = new(maxConcurrent, maxConcurrent);
    private int _running;
    private int _queued;

    public int MaxConcurrent { get; } = maxConcurrent;

    public int Running => Volatile.Read(ref _running);

    /// <summary>実行枠を待っている本数。走り出したものは含めない。</summary>
    public int Queued => Volatile.Read(ref _queued);

    /// <summary>実行枠を取る。空いていなければ待つ。返り値を破棄すると枠が空く。</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        // 空いているなら待ち行列を通らない。ここを省くと、
        // 空いているのに /health の queued が一瞬 1 に見える
        if (_slots.Wait(0, CancellationToken.None))
        {
            return Occupy();
        }

        Interlocked.Increment(ref _queued);
        try
        {
            if (!await _slots.WaitAsync(queueTimeout, cancellationToken).ConfigureAwait(false))
            {
                throw new ContractException(
                    StatusCodes.Status503ServiceUnavailable,
                    ErrorCodes.EngineBusy,
                    "エンジンが混雑しています。",
                    $"{queueTimeout.TotalSeconds:F0} 秒待っても実行枠が空きませんでした。");
            }
        }
        finally
        {
            Interlocked.Decrement(ref _queued);
        }

        return Occupy();
    }

    public void Dispose() => _slots.Dispose();

    private IDisposable Occupy()
    {
        Interlocked.Increment(ref _running);
        return new Slot(this);
    }

    private void Leave()
    {
        Interlocked.Decrement(ref _running);
        _slots.Release();
    }

    private sealed class Slot(SynthesisGate gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            // 二重解放でセマフォの上限を超えないようにする
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Leave();
            }
        }
    }
}
