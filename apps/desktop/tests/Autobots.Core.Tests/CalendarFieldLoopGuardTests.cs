using Autobots.Contracts;
using Autobots.Desktop;
using Xunit;

namespace Autobots.Core.Tests;

public sealed class CalendarFieldLoopGuardTests
{
    [Fact]
    public void RejectsSixthDateTimePickerClickAndAcceptsTypingAsRecovery()
    {
        var guard = new CalendarFieldLoopGuard();
        var click = new ClickAction(200, 150, PointerButton.Left);
        const string title = "Google Calendar - Create event — Mozilla Firefox";
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.True(guard.CanExecute(click, title, "Click the end time field"));
            guard.RecordExecuted(click, title, "Click the end time field");
        }

        Assert.False(guard.CanExecute(click, title, "Click the end date calendar popup"));
        Assert.True(guard.CanExecute(new KeyPressAction("Escape"), title, "Close the picker"));
        var type = new TypeTextAction("11:15 AM", false);
        Assert.True(guard.CanExecute(type, title, "Enter end time"));
        guard.RecordExecuted(type, title, "Enter end time");
        Assert.True(guard.CanExecute(click, title, "Click the end date field"));
    }

    [Fact]
    public void DoesNotInterfereWithOtherAppsOrUnrelatedCalendarControls()
    {
        var guard = new CalendarFieldLoopGuard();
        var click = new ClickAction(200, 150, PointerButton.Left);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            Assert.True(guard.CanExecute(click, "Google Calendar - Event details", "Click Add Google Meet"));
            guard.RecordExecuted(click, "Google Calendar - Event details", "Click Add Google Meet");
        }
        Assert.True(guard.CanExecute(click, "Notepad", "Click the date"));
    }
}
