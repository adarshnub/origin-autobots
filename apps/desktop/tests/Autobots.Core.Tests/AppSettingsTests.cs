using Autobots.Desktop;
using Xunit;

namespace Autobots.Core.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void DefaultAllowsLongerTasksWithinTheOwnerLimit()
    {
        var settings = new AppSettings().Normalized();
        Assert.Equal(100, settings.MaxStepsPerTask);
        Assert.Equal(200, AppSettings.MaxSteps);
        Assert.Equal(200, (settings with { MaxStepsPerTask = 250 }).Normalized().MaxStepsPerTask);
    }
}
