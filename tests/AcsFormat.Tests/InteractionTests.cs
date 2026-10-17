using OfficeAssistant.Core.Interaction;
using OfficeAssistant.Core.Platform;
using OfficeAssistant.Core.Rendering;

namespace AcsFormat.Tests;

/// <summary>The character's and balloon's mouse and keyboard handling, against a fake platform.</summary>
public sealed class InteractionTests : IDisposable
{
    private readonly SynchronizationContext? _previousContext = SynchronizationContext.Current;
    private readonly FakePlatform _platform = new();

    public InteractionTests() => SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());

    public void Dispose() => SynchronizationContext.SetSynchronizationContext(_previousContext);

    [Fact]
    public void ClickingTheCharacterWithoutMovingIsAClick()
    {
        using var character = new CharacterInteraction(_platform);
        var surface = _platform.Surfaces.Single();
        var clicks = 0;
        character.Clicked += (_, _) => clicks++;

        surface.Press(100, 100);
        Assert.True(surface.HasCapture);
        surface.MovePointer(102, 101); // Within the drag threshold.
        surface.Release(102, 101);

        Assert.Equal(1, clicks);
        Assert.False(surface.HasCapture);
        Assert.Equal((0, 0), (surface.X, surface.Y));
    }

    [Fact]
    public void DraggingMovesTheCharacterAndIsNotAClick()
    {
        using var character = new CharacterInteraction(_platform);
        var surface = _platform.Surfaces.Single();
        surface.MoveTo(50, 60);
        var clicks = 0;
        var dragStarts = 0;
        var moves = 0;
        character.Clicked += (_, _) => clicks++;
        character.DragStarted += (_, _) => dragStarts++;
        character.Moved += (_, _) => moves++;

        surface.Press(70, 80);
        surface.MovePointer(90, 85);
        surface.MovePointer(100, 95);
        surface.Release(100, 95);
        surface.MovePointer(200, 200); // No longer pressed.

        Assert.Equal((80, 75), (surface.X, surface.Y));
        Assert.Equal(1, dragStarts);
        Assert.Equal(2, moves);
        Assert.Equal(0, clicks);
    }

    [Fact]
    public void RightClickAsksForTheMenuAtThePointer()
    {
        using var character = new CharacterInteraction(_platform);
        var surface = _platform.Surfaces.Single();
        (int X, int Y)? requested = null;
        character.ContextMenuRequested += (_, point) => requested = point;

        surface.RightClick(321, 123);
        surface.MenuChoice = 4;

        Assert.Equal((321, 123), requested);
        Assert.Equal(4, character.ShowMenu(321, 123, [new MenuItem(4, "&Animate!")]));
        Assert.Equal("&Animate!", surface.LastMenu!.Single().Text);
    }

    [Fact]
    public void TypingReplacesThePlaceholderAndEditsTheQuestion()
    {
        var (balloon, surface) = ShowAnswerWizard();
        Assert.True(surface.IsVisible);
        Assert.True(surface.IsActive);
        Assert.Equal(string.Empty, balloon.Question); // Still the placeholder.

        surface.Type("prinx\b");
        surface.Type("t a document");
        Assert.Equal("print a document", balloon.Question);

        surface.Key(Key.Left, KeyModifiers.Word);
        surface.Type("X");
        Assert.Equal("print a Xdocument", balloon.Question);

        surface.Key(Key.Home);
        surface.Key(Key.Delete);
        Assert.Equal("rint a Xdocument", balloon.Question);
    }

    [Fact]
    public void ClipboardShortcutsUseThePlatformClipboard()
    {
        var (balloon, surface) = ShowAnswerWizard();
        surface.Type("hello world");

        surface.Key(Key.A, KeyModifiers.Command);
        surface.Key(Key.C, KeyModifiers.Command);
        Assert.Equal("hello world", _platform.FakeClipboard.Text);

        surface.Key(Key.End);
        surface.Key(Key.Left, KeyModifiers.Shift | KeyModifiers.Word);
        surface.Key(Key.X, KeyModifiers.Command);
        Assert.Equal("world", _platform.FakeClipboard.Text);
        Assert.Equal("hello ", balloon.Question);

        _platform.FakeClipboard.Text = "big\r\nwide";
        surface.Key(Key.V, KeyModifiers.Command);
        Assert.Equal("hello big wide", balloon.Question);
    }

    [Fact]
    public void EnterPressesTheDefaultButtonAndEscapeDismisses()
    {
        var (balloon, surface) = ShowAnswerWizard();
        var buttons = new List<string>();
        var dismissed = 0;
        balloon.ButtonClicked += (_, id) => buttons.Add(id);
        balloon.Dismissed += (_, _) => dismissed++;

        surface.Type("\r");
        surface.Key(Key.Escape);

        Assert.Equal([BalloonScenarios.SearchButton], buttons);
        Assert.Equal(1, dismissed);
    }

    [Fact]
    public void LosingFocusStopsTyping()
    {
        var (balloon, surface) = ShowAnswerWizard();
        surface.Type("a");
        surface.Deactivate();
        surface.Type("b");
        surface.Key(Key.A, KeyModifiers.Command);
        surface.Key(Key.C, KeyModifiers.Command);

        Assert.Equal("a", balloon.Question);
        Assert.Null(_platform.FakeClipboard.Text);
    }

    [Fact]
    public void ButtonsFireOnlyWhenPressedAndReleasedOnThem()
    {
        var (balloon, surface) = ShowAnswerWizard();
        var buttons = new List<string>();
        balloon.ButtonClicked += (_, id) => buttons.Add(id);

        // The Search button spans x 114..177 and y 86..107 of the balloon (6 px per character).
        surface.Click(surface.X + 120, surface.Y + 90);
        surface.Press(surface.X + 120, surface.Y + 90);
        surface.Release(surface.X + 20, surface.Y + 90); // Released over Options.

        Assert.Equal([BalloonScenarios.SearchButton], buttons);
    }

    [Fact]
    public void ClickingAndDraggingInTheTextBoxSelects()
    {
        var (balloon, surface) = ShowAnswerWizard();
        surface.Type("abcdefghij");
        surface.Deactivate();

        // Text starts 7 px into the balloon; characters are 6 px wide.
        surface.Press(surface.X + 7 + 12, surface.Y + 45);
        Assert.True(surface.IsActive);
        Assert.True(surface.HasCapture);
        surface.MovePointer(surface.X + 7 + 36, surface.Y + 45);
        surface.Release(surface.X + 7 + 36, surface.Y + 45);
        Assert.False(surface.HasCapture);

        surface.Key(Key.C, KeyModifiers.Command);
        Assert.Equal("cdef", _platform.FakeClipboard.Text);
        Assert.Equal(CursorKind.IBeam, surface.CursorProvider!());

        surface.DoubleClick(surface.X + 7 + 3, surface.Y + 45);
        surface.Key(Key.C, KeyModifiers.Command);
        Assert.Equal("abcdefghij", _platform.FakeClipboard.Text);
    }

    [Fact]
    public void TheBalloonSitsAboveTheCharacterUnlessThereIsNoRoom()
    {
        var (_, surface) = ShowAnswerWizard();
        Assert.True(surface.Y + surface.Height <= 500 + 9);

        using var low = new BalloonInteraction(_platform);
        var lowSurface = _platform.Surfaces.Last();
        low.ShowContent(BalloonScenarios.AnswerWizard(), (1000, 20, 124, 93), 1, activate: false);
        Assert.True(lowSurface.Y >= 20 + 93 - 9);
        Assert.False(lowSurface.IsActive);
    }

    private (BalloonInteraction Balloon, FakeSurface Surface) ShowAnswerWizard()
    {
        var balloon = new BalloonInteraction(_platform);
        var surface = _platform.Surfaces.Last();
        balloon.ShowContent(BalloonScenarios.AnswerWizard(), (1000, 500, 124, 93), 1, activate: true);
        return (balloon, surface);
    }
}
