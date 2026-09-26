using Autobots.Desktop;
using Xunit;

namespace Autobots.Core.Tests;

public sealed class OwnerResumeGateTests
{
    [Fact]
    public async Task OwnerCanContinueSameWaitOnceAndASecondHandoffCanWaitAgain()
    {
        var gate = new OwnerResumeGate();
        Assert.False(gate.Continue());
        var first = gate.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.True(gate.IsPending);
        Assert.True(gate.Continue());
        Assert.False(gate.Continue());
        Assert.True(await first);
        Assert.False(gate.IsPending);
        var second = gate.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.True(gate.Continue());
        Assert.True(await second);
    }

    [Fact]
    public async Task StopCancelsTheWaitAndLateContinueIsRejected()
    {
        var gate = new OwnerResumeGate();
        using var stop = new CancellationTokenSource();
        var pending = gate.WaitAsync(TimeSpan.FromSeconds(2), stop.Token);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(gate.IsPending);
        Assert.False(gate.Continue());
    }

    [Fact]
    public async Task TimeLimitClearsTheWait()
    {
        var gate = new OwnerResumeGate();
        Assert.False(await gate.WaitAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None));
        Assert.False(gate.IsPending);
        Assert.False(gate.Continue());
    }
}
