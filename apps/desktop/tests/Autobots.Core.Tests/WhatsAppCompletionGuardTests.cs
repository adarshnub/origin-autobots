using Autobots.Desktop;
using Xunit;

namespace Autobots.Core.Tests;

public sealed class WhatsAppCompletionGuardTests
{
    [Theory]
    [InlineData("Paste an unsent draft in WhatsApp")]
    [InlineData("Share the link via whatsapp desktop")]
    public void RequiresIndependentReviewForWhatsApp(string instruction)
    {
        Assert.True(WhatsAppCompletionGuard.RequiresReview(instruction));
    }

    [Fact]
    public void DoesNotChangeOtherTasks()
    {
        Assert.False(WhatsAppCompletionGuard.RequiresReview("Write a greeting in Notepad"));
        Assert.False(WhatsAppCompletionGuard.RequiresReview("Write a greeting in Notepad. Do not copy anything or open WhatsApp."));
        Assert.True(WhatsAppCompletionGuard.RequiresReview("Paste the greeting into WhatsApp; do not send it."));
    }
}
