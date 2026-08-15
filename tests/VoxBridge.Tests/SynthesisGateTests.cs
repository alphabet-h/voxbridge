using VoxBridge.Contract;
using VoxBridge.Hosting;

namespace VoxBridge.Tests;

public class SynthesisGateTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Immediate = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task 空いていれば待たずに通す()
    {
        using var gate = new SynthesisGate(1, Generous);

        using var slot = await gate.EnterAsync(CancellationToken.None);

        Assert.Equal(1, gate.Running);
        Assert.Equal(0, gate.Queued);
    }

    [Fact]
    public async Task 解放すると枠が戻る()
    {
        using var gate = new SynthesisGate(1, Generous);

        (await gate.EnterAsync(CancellationToken.None)).Dispose();

        Assert.Equal(0, gate.Running);
        using var second = await gate.EnterAsync(CancellationToken.None);
        Assert.Equal(1, gate.Running);
    }

    [Fact]
    public async Task 上限まで同時に走れる()
    {
        using var gate = new SynthesisGate(3, Generous);

        var slots = new List<IDisposable>();
        for (var i = 0; i < 3; i++)
        {
            slots.Add(await gate.EnterAsync(CancellationToken.None));
        }

        Assert.Equal(3, gate.Running);
        Assert.Equal(0, gate.Queued);

        foreach (var slot in slots)
        {
            slot.Dispose();
        }
    }

    [Fact]
    public async Task 空くまで待つ()
    {
        using var gate = new SynthesisGate(1, Generous);
        var held = await gate.EnterAsync(CancellationToken.None);

        var waiting = gate.EnterAsync(CancellationToken.None);
        await WaitUntil(() => gate.Queued == 1);

        Assert.Equal(1, gate.Running);
        Assert.Equal(1, gate.Queued);

        held.Dispose();

        using var second = await waiting;
        Assert.Equal(1, gate.Running);
        Assert.Equal(0, gate.Queued);
    }

    [Fact]
    public async Task 待ちきれなければ_503_ENGINE_BUSY()
    {
        using var gate = new SynthesisGate(1, Immediate);
        using var held = await gate.EnterAsync(CancellationToken.None);

        var error = await Assert.ThrowsAsync<ContractException>(
            () => gate.EnterAsync(CancellationToken.None));

        Assert.Equal(503, error.StatusCode);
        Assert.Equal(ErrorCodes.EngineBusy, error.Code);

        // 溢れた側が枠を掴んだままにならないこと
        Assert.Equal(1, gate.Running);
        Assert.Equal(0, gate.Queued);
    }

    [Fact]
    public async Task 切断されたら待ち行列から即座に外れる()
    {
        // ここを怠ると、連打されたぶんだけ枠が埋まり以降が全部 503 になる
        using var gate = new SynthesisGate(1, Generous);
        using var held = await gate.EnterAsync(CancellationToken.None);

        using var disconnected = new CancellationTokenSource();
        var waiting = gate.EnterAsync(disconnected.Token);
        await WaitUntil(() => gate.Queued == 1);

        await disconnected.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await WaitUntil(() => gate.Queued == 0);

        Assert.Equal(0, gate.Queued);
        Assert.Equal(1, gate.Running);
    }

    [Fact]
    public async Task 二重に解放しても枠が増えない()
    {
        using var gate = new SynthesisGate(1, Immediate);

        var slot = await gate.EnterAsync(CancellationToken.None);
        slot.Dispose();
        slot.Dispose();

        Assert.Equal(0, gate.Running);

        // 上限が 1 のままであること（2 本目は溢れる）
        using var first = await gate.EnterAsync(CancellationToken.None);
        await Assert.ThrowsAsync<ContractException>(() => gate.EnterAsync(CancellationToken.None));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }
}
