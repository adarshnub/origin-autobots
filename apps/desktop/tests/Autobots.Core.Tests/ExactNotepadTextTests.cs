using Autobots.Desktop;
using Xunit;

namespace Autobots.Core.Tests;

public sealed class ExactNotepadTextTests
{
    [Fact]
    public void ExtractsExactStoryFromOwnerInstruction()
    {
        const string story = "Hi Amal! A tiny robot found a paper star.";
        var instruction = $"Open Notepad and type this exact greeting story:\n{story}\nSelect all and copy it.";
        Assert.Equal(story, ExactNotepadText.FromOwnerInstruction(instruction));
        Assert.True(ExactNotepadText.IsNotepad("Notepad.exe"));
    }

    [Theory]
    [InlineData("Open Notepad and type something nice")]
    [InlineData("Open Notepad and type this exact greeting story:")]
    [InlineData("Create a note in Notepad")]
    public void DoesNotInventExactText(string instruction)
    {
        Assert.Null(ExactNotepadText.FromOwnerInstruction(instruction));
    }
}
