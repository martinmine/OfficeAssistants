using AcsFormat;
using OfficeAssistant.Core.Animation;
using OfficeAssistant.Core.Rendering;
using OfficeAssistant.Core.Windows;

namespace OfficeAssistant.Core;

/// <summary>
/// Runs the on-screen assistant: the character window, its balloon, animations,
/// sounds, idling and the Answer Wizard. Create and use it on the UI thread.
/// </summary>
public sealed class AssistantController : IDisposable
{
    private const int MenuHide = 1;
    private const int MenuOptions = 2;
    private const int MenuChoose = 3;
    private const int MenuAnimate = 4;
    private const int ResultsPageSize = 5;

    private static readonly string[] NonGestureAnimations = ["Show", "Hide", "RestPose", "Hearing_1", "Alert", "GoodBye"];

    private readonly CharacterWindow _window = new();
    private readonly BalloonWindow _balloon = new();
    private readonly UiTimer _idleTimer = new();
    private readonly Random _random = new();

    private AnimationPlayer? _player;
    private SoundPlayer? _sounds;
    private uint[]? _lastFrame;
    private bool _positioned;
    private bool _hiding;
    private DateTime _idleSince = DateTime.UtcNow;
    private IReadOnlyList<HelpTopics.Topic> _results = [];
    private int _resultsPage;
    private int _scale = 1;
    private bool _soundsEnabled = true;

    public AssistantController()
    {
        _window.Clicked += (_, _) => OnCharacterClicked();
        _window.DoubleClicked += (_, _) => Animate();
        _window.ContextMenuRequested += (_, point) => ShowContextMenu(point.X, point.Y);
        _window.Moved += (_, _) => _balloon.UpdateAnchor(FrameBounds, _scale);
        _balloon.ButtonClicked += (_, id) => OnBalloonButton(id);
        _balloon.LabelClicked += (_, id) => OnBalloonLabel(id);
        _balloon.Dismissed += (_, _) => CloseBalloon();
    }

    public event EventHandler? VisibilityChanged;

    public event EventHandler? ChooseAssistantRequested;

    public event EventHandler? OptionsRequested;

    /// <summary>Raised with the animation name whenever one starts.</summary>
    public event EventHandler<string>? AnimationStarted;

    public AssistantDefinition? Definition { get; private set; }

    public AcsCharacter? Character { get; private set; }

    public bool IsShown => _window.IsVisible && !_hiding;

    /// <summary>Windows language id used for the character's localized strings.</summary>
    public int LanguageId { get; set; } = System.Globalization.CultureInfo.CurrentUICulture.LCID;

    public AcsLocalizedInfo? Strings => Character?.LocalizedInfo.Count > 0 ? Character.GetLocalizedInfo(LanguageId) : null;

    public bool SoundsEnabled
    {
        get => _soundsEnabled;
        set
        {
            _soundsEnabled = value;
            if (_sounds is not null)
            {
                _sounds.Enabled = value;
            }
        }
    }

    /// <summary>Integer pixel enlargement (1 = identical to the original).</summary>
    public int Scale
    {
        get => _scale;
        set
        {
            value = Math.Clamp(value, 1, 4);
            if (value == _scale)
            {
                return;
            }

            // Keep the character's feet where they are.
            var oldBottom = _window.Y + _window.Height;
            _scale = value;
            if (_lastFrame is not null && Character is not null)
            {
                Present(_lastFrame, _window.X, oldBottom - (Character.Height * value));
                _balloon.UpdateAnchor(FrameBounds, _scale);
            }
        }
    }

    private (int X, int Y, int Width, int Height) FrameBounds =>
        (_window.X, _window.Y, (Character?.Width ?? 0) * _scale, (Character?.Height ?? 0) * _scale);

    /// <summary>Switches to another assistant, keeping it on screen if one was shown.</summary>
    public void Load(AssistantDefinition definition, bool greet = true)
    {
        var wasShown = IsShown;
        UnloadCharacter();

        Definition = definition;
        Character = definition.LoadCharacter();
        _sounds = new SoundPlayer(Character) { Enabled = SoundsEnabled };
        _player = new AnimationPlayer(Character, frame => Present(frame, _window.X, _window.Y), _sounds.Play);

        if (wasShown)
        {
            Show(greet);
        }
    }

    public void Show(bool greet = true)
    {
        if (Character is null || _player is null)
        {
            return;
        }

        if (!_positioned)
        {
            var work = LayeredWindow.GetWorkArea(0, 0);
            var x = work.Right - (Character.Width * _scale) - 40;
            var y = work.Bottom - (Character.Height * _scale) - 40;
            _window.MoveTo(x, y);
            _positioned = true;
        }

        _hiding = false;
        var showAnimation = Character.GetStateAnimations("SHOWING").FirstOrDefault() ?? "Show";
        _player.Abort();
        if (Character.FindAnimation(showAnimation) is not null)
        {
            _player.ShowStill(showAnimation);
        }
        else
        {
            ShowRestPose();
        }

        _window.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
        PlayInternal(showAnimation, () =>
        {
            if (greet && Strings?.Greetings is { Count: > 0 } greetings)
            {
                ShowBalloon(BalloonScenarios.TipWithAnswerWizard(greetings[_random.Next(greetings.Count)]), activate: false);
            }
        });
    }

    public void Hide()
    {
        if (!_window.IsVisible || Character is null)
        {
            return;
        }

        CloseBalloon();
        _idleTimer.Stop();
        _hiding = true;
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
        var hideAnimation = Character.GetStateAnimations("HIDING").FirstOrDefault() ?? "Hide";
        if (!PlayInternal(hideAnimation, HideNow))
        {
            HideNow();
        }
    }

    /// <summary>Plays a named animation on the visible assistant.</summary>
    public bool Play(string animation)
    {
        if (!IsShown)
        {
            return false;
        }

        _idleSince = DateTime.UtcNow;
        return PlayInternal(animation, null);
    }

    /// <summary>The context menu's "Animate!": a random gesture.</summary>
    public void Animate()
    {
        if (Character is null)
        {
            return;
        }

        var candidates = Character.Animations
            .Where(a => !a.Name.StartsWith("Idle", StringComparison.OrdinalIgnoreCase)
                && !a.Name.StartsWith("Look", StringComparison.OrdinalIgnoreCase)
                && !a.Name.StartsWith("Gesture", StringComparison.OrdinalIgnoreCase)
                && !NonGestureAnimations.Contains(a.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
        {
            candidates = [.. Character.Animations];
        }

        Play(candidates[_random.Next(candidates.Count)].Name);
    }

    public void ShowBalloon(BalloonContent content, bool activate = true)
    {
        if (!IsShown)
        {
            return;
        }

        _balloon.ShowContent(content, FrameBounds, _scale, activate);
    }

    public void CloseBalloon() => _balloon.Close();

    public void Dispose()
    {
        UnloadCharacter();
        _idleTimer.Dispose();
        _balloon.Dispose();
        _window.Dispose();
    }

    private void UnloadCharacter()
    {
        _idleTimer.Stop();
        _balloon.Close();
        _player?.Abort();
        _player?.Dispose();
        _sounds?.Dispose();
        _player = null;
        _sounds = null;
        _lastFrame = null;
        _window.Hide();
        Character = null;
    }

    private void HideNow()
    {
        _window.Hide();
        _hiding = false;
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool PlayInternal(string animation, Action? completed)
    {
        if (_player is null)
        {
            return false;
        }

        _idleTimer.Stop();
        var started = _player.Play(animation, () =>
        {
            completed?.Invoke();
            ScheduleIdle();
        });
        if (started)
        {
            AnimationStarted?.Invoke(this, Character?.FindAnimation(animation)?.Name ?? animation);
        }

        return started;
    }

    private void ShowRestPose()
    {
        if (Character is null || _player is null)
        {
            return;
        }

        var rest = Character.FindAnimation("RestPose") ?? Character.Animations.FirstOrDefault();
        if (rest is not null)
        {
            _player.ShowStill(rest.Name);
        }
    }

    private void ScheduleIdle()
    {
        if (Character is null || _hiding || !_window.IsVisible || _player?.IsPlaying == true)
        {
            return;
        }

        // Gallery-only assistants have nothing but their wave, so idle less often.
        var (min, max) = Character.Animations.Count > 3 ? (6_000, 12_000) : (25_000, 45_000);
        _idleTimer.Start(_random.Next(min, max), PlayIdle);
    }

    private void PlayIdle()
    {
        if (Character is null || !IsShown)
        {
            return;
        }

        var idleSeconds = (DateTime.UtcNow - _idleSince).TotalSeconds;
        var level = idleSeconds < 60 ? 1 : idleSeconds < 180 ? 2 : 3;
        IReadOnlyList<string> animations = [];
        for (; level >= 1 && animations.Count == 0; level--)
        {
            animations = Character.GetStateAnimations($"IDLINGLEVEL{level}");
        }

        if (animations.Count == 0)
        {
            return;
        }

        PlayInternal(animations[_random.Next(animations.Count)], null);
    }

    private void Present(uint[] frame, int x, int y)
    {
        if (Character is null)
        {
            return;
        }

        _lastFrame = frame;
        _window.Present(frame, Character.Width, Character.Height, x, y, _scale);
    }

    private void OnCharacterClicked()
    {
        _idleSince = DateTime.UtcNow;
        if (_balloon.IsVisible)
        {
            CloseBalloon();
            return;
        }

        ShowBalloon(BalloonScenarios.AnswerWizard());
    }

    private void ShowContextMenu(int x, int y)
    {
        var choice = _window.ShowMenu(x, y,
        [
            (MenuHide, "&Hide", true),
            (MenuOptions, "&Options...", true),
            (MenuChoose, "&Choose Assistant...", true),
            (MenuAnimate, "&Animate!", true),
        ]);

        switch (choice)
        {
            case MenuHide:
                Hide();
                break;
            case MenuOptions:
                OptionsRequested?.Invoke(this, EventArgs.Empty);
                break;
            case MenuChoose:
                ChooseAssistantRequested?.Invoke(this, EventArgs.Empty);
                break;
            case MenuAnimate:
                Animate();
                break;
        }
    }

    private void OnBalloonButton(string id)
    {
        _idleSince = DateTime.UtcNow;
        switch (id)
        {
            case BalloonScenarios.SearchButton:
                Search(_balloon.Question);
                break;
            case BalloonScenarios.OptionsButton:
                CloseBalloon();
                OptionsRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "yes":
                CloseBalloon();
                Play("Save");
                break;
            default:
                CloseBalloon();
                break;
        }
    }

    private void OnBalloonLabel(string id)
    {
        _idleSince = DateTime.UtcNow;
        if (id == BalloonScenarios.SeeMoreId)
        {
            _resultsPage = (_resultsPage + 1) * ResultsPageSize < _results.Count ? _resultsPage + 1 : 0;
            ShowResultsPage();
            return;
        }

        CloseBalloon();
        var topic = HelpTopics.All.FirstOrDefault(t => t.Id == id);
        if (id == "letter-help")
        {
            Play("Writing");
        }
        else if (topic is not null && !Play(topic.Animation))
        {
            Animate();
        }
    }

    private void Search(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return;
        }

        _results = HelpTopics.Search(question);
        _resultsPage = 0;
        if (_results.Count == 0)
        {
            Play("Thinking");
            ShowBalloon(new BalloonContent(
                new BalloonText("What would you like to do?", Bold: true),
                new BalloonText("I don't know what you mean. Please rephrase your question."),
                new BalloonTextBox(question),
                new BalloonButtons([new BalloonButton("&Options", BalloonScenarios.OptionsButton), new BalloonButton("&Search", BalloonScenarios.SearchButton)]) { DefaultIndex = 1 }));
            return;
        }

        Play("Searching");
        ShowResultsPage(question);
    }

    private void ShowResultsPage(string? question = null)
    {
        var page = _results.Skip(_resultsPage * ResultsPageSize).Take(ResultsPageSize).Select(t => (t.Id, t.Title)).ToList();
        var content = BalloonScenarios.SearchResults(page, hasMore: _results.Count > ResultsPageSize);
        var text = question ?? _balloon.Question;
        ShowBalloon(new BalloonContent(
            [.. content.Blocks.Select(b => b is BalloonTextBox box ? box with { InitialText = text } : b)]));
    }
}
