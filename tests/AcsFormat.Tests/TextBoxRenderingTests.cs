using OfficeAssistant.Core.Rendering;

namespace AcsFormat.Tests;

public class TextBoxRenderingTests
{
    [Fact]
    public void ClicksMapToCharacterPositions()
    {
        using var renderer = new BalloonRenderer(new BitmapFontRenderer());
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
        using var renderer = new BalloonRenderer(new BitmapFontRenderer());
        var state = new BalloonState { TextBoxText = "Print merged documents", TextBoxFocused = true };
        state.Select(6, 12);
        var layout = renderer.Measure(BalloonScenarios.AnswerWizard(), state);

        Assert.Contains(0xFF316AC5u, renderer.Render(layout, state, BalloonTailSide.Bottom, 90).Pixels);

        state.TextBoxFocused = false;
        Assert.DoesNotContain(0xFF316AC5u, renderer.Render(layout, state, BalloonTailSide.Bottom, 90).Pixels);
    }
}
