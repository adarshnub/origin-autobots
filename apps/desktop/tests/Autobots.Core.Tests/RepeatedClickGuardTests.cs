using Autobots.Contracts;
using Autobots.Desktop;
using Xunit;

namespace Autobots.Core.Tests;

public sealed class RepeatedClickGuardTests
{
    [Fact]
    public void RejectsThirdIdenticalClickButAllowsNewVisualStateOrApplication()
    {
        var guard = new RepeatedClickGuard();
        var click = new ClickAction(70, 148, PointerButton.Left);
        byte[] image = [1, 2, 3];
        Assert.True(guard.CanExecute(click, image, 10));
        guard.RecordExecuted(click, image, 10);
        Assert.True(guard.CanExecute(click, image, 10));
        guard.RecordExecuted(click, image, 10);
        Assert.False(guard.CanExecute(click, image, 10));
        Assert.True(guard.CanExecute(click, [1, 2, 4], 10));
        Assert.True(guard.CanExecute(click, image, 11));
        Assert.True(guard.CanExecute(new ClickAction(300, 148, PointerButton.Left), image, 10));
        Assert.True(guard.CanExecute(new KeyPressAction("Escape"), image, 10));
    }

    [Fact]
    public void RejectedProposalsDoNotCountAsExecutedAndRecentHistoryIsBounded()
    {
        var guard = new RepeatedClickGuard();
        var click = new ClickAction(1, 1, PointerButton.Left);
        byte[] image = [0];
        for (var i = 0; i < 20; i++) Assert.True(guard.CanExecute(click, image, 1));
        guard.RecordExecuted(click, image, 1);
        guard.RecordExecuted(click, image, 1);
        for (var i = 0; i < 12; i++) guard.RecordExecuted(new ClickAction(i + 10, 1, PointerButton.Left), image, 1);
        Assert.True(guard.CanExecute(click, image, 1));
    }
}
