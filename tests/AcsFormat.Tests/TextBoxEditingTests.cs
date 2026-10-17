using OfficeAssistant.Core.Rendering;
using OfficeAssistant.Core.Interaction;

namespace AcsFormat.Tests;

public class TextBoxEditingTests
{
    [Theory]
    [InlineData("print a document", 2, 0, 6)] // "print " including its trailing space.
    [InlineData("print a document", 6, 6, 8)]
    [InlineData("print a document", 16, 8, 16)] // Past the end selects the last word.
    [InlineData("print  a", 5, 5, 7)] // On whitespace: the run of spaces.
    [InlineData("", 0, 0, 0)]
    public void DoubleClickSelectsWord(string text, int index, int start, int end)
    {
        Assert.Equal((start, end), TextEditing.WordAt(text, index));
    }

    [Fact]
    public void WordNavigation()
    {
        const string text = "print a  document";
        Assert.Equal(6, TextEditing.NextWordStart(text, 0));
        Assert.Equal(9, TextEditing.NextWordStart(text, 6));
        Assert.Equal(6, TextEditing.PreviousWordStart(text, 9));
        Assert.Equal(0, TextEditing.PreviousWordStart(text, 5));
    }

    [Fact]
    public void ReplacingASelectionMovesTheCaret()
    {
        var state = new BalloonState { TextBoxText = "print a document" };
        state.Select(6, 8);
        Assert.Equal("a ", state.SelectedText);

        state.ReplaceSelection("the ");

        Assert.Equal("print the document", state.TextBoxText);
        Assert.Equal(10, state.CaretIndex);
        Assert.False(state.HasSelection);
    }
}
