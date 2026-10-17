using OfficeAssistant.Core.Rendering;
using OfficeAssistant.Core.Windows;

namespace OfficeAssistant.Core;

/// <summary>Keeps the balloon next to the character and only shows it while the character is shown.</summary>
internal sealed class BalloonCoordinator : IDisposable
{
    private readonly BalloonWindow _window = new();
    private readonly CharacterPresenter _character;

    public BalloonCoordinator(CharacterPresenter character)
    {
        _character = character;
        _character.BoundsChanged += (_, _) => _window.UpdateAnchor(_character.FrameBounds, _character.Scale);
    }

    /// <summary>The balloon's window, for its button, label and dismiss events.</summary>
    public BalloonWindow Window => _window;

    public bool IsVisible => _window.IsVisible;

    /// <summary>Current text of the Answer Wizard box.</summary>
    public string Question => _window.Question;

    public void Show(BalloonContent content, bool activate = true)
    {
        if (!_character.IsShown)
        {
            return;
        }

        _window.ShowContent(content, _character.FrameBounds, _character.Scale, activate);
    }

    public void Close() => _window.Close();

    public void Dispose() => _window.Dispose();
}
