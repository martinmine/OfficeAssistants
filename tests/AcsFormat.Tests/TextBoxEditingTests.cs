using OfficeAssistant.Core.Rendering;
using OfficeAssistant.Core.Windows;

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

    [Fact]
    public void ClicksMapToCharacterPositions()
    {
        using var renderer = new BalloonRenderer();
        var state = new BalloonState { TextBoxText = "Print merged documents", TextBoxFocused = true };
        var layout = renderer.Measure(BalloonScenarios.AnswerWizard(), state);
        var bitmap = renderer.Render(layout, state, BalloonTailSide.Bottom, 90);

        // Text starts at (7, 38) in the balloon; each Tahoma 13 px glyph is a few pixels wide.
        Assert.Equal(0, renderer.TextIndexFromPoint(bitmap, 0, 40));
        Assert.Equal(state.TextBoxText.Length, renderer.TextIndexFromPoint(bitmap, 180, 40));
        var middle = renderer.TextIndexFromPoint(bitmap, 60, 40);
        Assert.InRange(middle, 6, 12);
    }

    [Fact]
    public void PartialSelectionIsHighlightedOnlyWhileFocused()
    {
        using var renderer = new BalloonRenderer();
        var state = new BalloonState { TextBoxText = "Print merged documents", TextBoxFocused = true };
        state.Select(6, 12);
        var layout = renderer.Measure(BalloonScenarios.AnswerWizard(), state);

        Assert.Contains(0xFF316AC5u, renderer.Render(layout, state, BalloonTailSide.Bottom, 90).Pixels);

        state.TextBoxFocused = false;
        Assert.DoesNotContain(0xFF316AC5u, renderer.Render(layout, state, BalloonTailSide.Bottom, 90).Pixels);
    }
}
