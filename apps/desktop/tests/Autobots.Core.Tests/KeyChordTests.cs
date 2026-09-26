using Autobots.Platform;
using Xunit;

namespace Autobots.Core.Tests;

public sealed class KeyChordTests
{
    [Theory]
    [InlineData("Control+L", KeyModifiers.Control, "l")]
    [InlineData("ctrl + shift + t", KeyModifiers.Control | KeyModifiers.Shift, "t")]
    [InlineData("Meta", KeyModifiers.None, "Meta")]
    [InlineData("win", KeyModifiers.None, "Meta")]
    [InlineData("Windows+R", KeyModifiers.Meta, "r")]
    [InlineData("Alt+Tab", KeyModifiers.Alt, "Tab")]
    [InlineData("Enter", KeyModifiers.None, "Enter")]
    [InlineData("Return", KeyModifiers.None, "Enter")]
    [InlineData("Page_Down", KeyModifiers.None, "PageDown")]
    [InlineData("ArrowLeft", KeyModifiers.None, "ArrowLeft")]
    [InlineData("shift+down", KeyModifiers.Shift, "ArrowDown")]
    [InlineData("F5", KeyModifiers.None, "F5")]
    [InlineData("alt+f24", KeyModifiers.Alt, "F24")]
    [InlineData("KeyA", KeyModifiers.None, "a")]
    [InlineData("Digit7", KeyModifiers.None, "7")]
    [InlineData("Ctrl++", KeyModifiers.Control, "+")]
    [InlineData("Ctrl+Minus", KeyModifiers.Control, "-")]
    [InlineData("ctrl+/", KeyModifiers.Control, "/")]
    [InlineData("Command+C", KeyModifiers.Control, "c")]
    [InlineData("AudioVolumeMute", KeyModifiers.None, "VolumeMute")]
    [InlineData("MediaPlayPause", KeyModifiers.None, "MediaPlayPause")]
    public void ModelKeyDescriptionsParseToCanonicalChords(string text, KeyModifiers modifiers, string key)
    {
        Assert.True(KeyChord.TryParse(text, out var chord, out var error), error);
        Assert.Equal(modifiers, chord.Modifiers);
        Assert.Equal(key, chord.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ctrl+shift")]
    [InlineData("a+b")]
    [InlineData("ctrl+ctrl+a")]
    [InlineData("Hyper+Q")]
    [InlineData("F25")]
    [InlineData("ctrl+alt+shift+win+x")]
    [InlineData("ScrollLock")]
    public void UnknownOrMalformedKeysFailClosed(string text)
    {
        Assert.False(KeyChord.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("Win+L")]
    [InlineData("Control+Alt+Delete")]
    [InlineData("Ctrl+Shift+Esc")]
    [InlineData("Win+Ctrl+Shift+B")]
    [InlineData("Ctrl+Alt+Shift+S")]
    [InlineData("Ctrl+Alt+Space")]
    public void SecurityAndAutobotsShortcutsAreBlocked(string text)
    {
        Assert.True(KeyChord.TryParse(text, out var chord, out var error), error);
        Assert.NotNull(chord.BlockedReason());
    }

    [Theory]
    [InlineData("Alt+Tab")]
    [InlineData("Ctrl+Alt+T")]
    [InlineData("Win")]
    [InlineData("Win+E")]
    [InlineData("Ctrl+S")]
    [InlineData("Escape")]
    public void OrdinaryShortcutsAreAllowed(string text)
    {
        Assert.True(KeyChord.TryParse(text, out var chord, out var error), error);
        Assert.Null(chord.BlockedReason());
    }

    [Fact]
    public void ChordsDescribeThemselvesForTheActivityLog()
    {
        Assert.True(KeyChord.TryParse("control+shift+t", out var chord, out _));
        Assert.Equal("Ctrl+Shift+T", chord.ToString());
        Assert.True(KeyChord.TryParse("meta", out var start, out _));
        Assert.Equal("Win", start.ToString());
    }

    [Theory]
    [InlineData(1920, 1080, 1440, 810)]
    [InlineData(2560, 1600, 1440, 900)]
    [InlineData(3840, 2160, 1440, 810)]
    [InlineData(1280, 720, 1280, 720)]
    [InlineData(1080, 1920, 506, 900)]
    public void ObservationImagesFitTheModelResolutionWithoutDistortion(int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), DisplayCoordinateTransform.FitWithin(width, height, 1440, 900));
    }
}
