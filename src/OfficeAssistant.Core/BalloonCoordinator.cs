using OfficeAssistant.Core.Rendering;

namespace OfficeAssistant.Core;

/// <summary>Keeps the balloon next to the character and only shows it while the character is shown.</summary>
internal sealed class BalloonCoordinator : IDisposable
{
    private readonly IBalloonView _window;
    private readonly CharacterPresenter _character;

    public BalloonCoordinator(IBalloonView window, CharacterPresenter character)
    {
        _window = window;
        _character = character;
        _character.BoundsChanged += (_, _) =>
        {
            if (_window.IsVisible)
            {
                _window.UpdateFrame(Frame);
            }
        };
    }

    /// <summary>The balloon's window, for its button, label and dismiss events.</summary>
    public IBalloonView Window => _window;

    public bool IsVisible => _window.IsVisible;

    /// <summary>Current text of the Answer Wizard box.</summary>
    public string Question => _window.Question;

    public void Show(BalloonContent content, bool activate = true)
    {
        if (!_character.IsShown)
        {
            return;
        }

        _window.Show(content, Frame, activate);
    }

    public void Close() => _window.Close();

    public void Dispose() => _window.Dispose();

    private BalloonFrame Frame
    {
        get
        {
            var (x, y, width, height) = _character.FrameBounds;
            return new BalloonFrame(x, y, width, height, _character.Scale);
        }
    }
}
