using OfficeAssistant.Core.Interaction;
using OfficeAssistant.Core.Platform;
using OfficeAssistant.Core.Rendering;

namespace OfficeAssistant.Core;

/// <summary>Keeps the balloon next to the character and only shows it while the character is shown.</summary>
internal sealed class BalloonCoordinator : IDisposable
{
    private readonly BalloonInteraction _window;
    private readonly CharacterPresenter _character;

    public BalloonCoordinator(IPlatform platform, CharacterPresenter character)
    {
        _window = new BalloonInteraction(platform);
        _character = character;
        _character.BoundsChanged += (_, _) => _window.UpdateAnchor(_character.FrameBounds, _character.Scale);
    }

    /// <summary>The balloon, for its button, label and dismiss events.</summary>
    public BalloonInteraction Window => _window;

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
