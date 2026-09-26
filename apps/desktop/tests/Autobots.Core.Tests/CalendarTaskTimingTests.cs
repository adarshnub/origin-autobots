using Autobots.Desktop;
using Xunit;

namespace Autobots.Core.Tests;

public sealed class CalendarTaskTimingTests
{
    [Fact]
    public void ParsesExplicitOwnerGrantAndMatchesAccessibleValues()
    {
        var timing = CalendarTaskTiming.FromOwnerInstruction(
            "Set start date September 27, 2026, start time 11:00 AM, end date September 27, 2026, end time 11:15 AM, in Asia/Kolkata.");
        Assert.NotNull(timing);
        Assert.Equal("Sep 27, 2026", timing.TextFor("Start date"));
        Assert.Equal("11:15 AM", timing.TextFor("End time"));
        Assert.True(timing.Matches("Start date", "Sep 27, 2026"));
        Assert.True(timing.Matches("End time", "11:15am"));
        Assert.False(timing.Matches("Start time", "6:00pm"));
        Assert.False(timing.Matches("End date", "Sep 28, 2026"));
    }

    [Theory]
    [InlineData("Set start date September 27, 2026, start time 11:00 AM, end date September 27, 2026, end time 10:00 AM")]
    [InlineData("Set start date September 27, 2026, start time 11:00 AM, end date September 29, 2026, end time 11:15 AM")]
    [InlineData("Schedule something tomorrow at 11")]
    public void RejectsUnclearOrInvalidTimes(string instruction)
    {
        Assert.Null(CalendarTaskTiming.FromOwnerInstruction(instruction));
    }
}
