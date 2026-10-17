using AcsFormat;
using OfficeAssistant.Core.Animation;
using OfficeAssistant.Core.Interaction;
using OfficeAssistant.Core.Platform;

namespace OfficeAssistant.Core;

/// <summary>
/// The character on screen: its window, animations, sounds, idling and pixel size.
/// Create and use it on the UI thread.
/// </summary>
internal sealed class CharacterPresenter : IDisposable
{
    private readonly IPlatform _platform;
    private readonly CharacterInteraction _interaction;
    private readonly IFloatingSurface _window;
    private readonly UiTimer _idleTimer = new();
    private readonly Random _random = new();

    private AnimationPlayer? _player;
    private SoundPlayer? _sounds;
    private uint[]? _lastFrame;
    private bool _positioned;
    private bool _hiding;
    private DateTime _idleSince = DateTime.UtcNow;
    private int _scale = 1;
    private int? _preferredScale;
    private bool _soundsEnabled = true;

    public CharacterPresenter(IPlatform platform, int? preferredScale)
    {
        _platform = platform;
        _interaction = new CharacterInteraction(platform);
        _window = _interaction.Surface;
        _preferredScale = ClampScale(preferredScale);
        _interaction.Moved += (_, _) => OnMoved();
    }

    public event EventHandler? VisibilityChanged;

    /// <summary>Raised with the animation name whenever one starts.</summary>
    public event EventHandler<string>? AnimationStarted;

    /// <summary>Raised when the character moved or changed size, so the balloon can follow.</summary>
    public event EventHandler? BoundsChanged;

    /// <summary>The character's mouse handling, for its click and menu events.</summary>
    public CharacterInteraction Interaction => _interaction;

    public AcsCharacter? Character { get; private set; }

    public bool IsShown => _window.IsVisible && !_hiding;

    /// <summary>True while the character is on screen, including while its hide animation plays.</summary>
    public bool IsOnScreen => _window.IsVisible;

    public bool IsPlaying => _player?.IsPlaying == true;

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

    /// <summary>Integer pixel enlargement in use (1 = identical to the original).</summary>
    public int Scale => _scale;

    /// <summary>The pixel size the user picked, or null to follow the display.</summary>
    public int? PreferredScale
    {
        get => _preferredScale;
        set
        {
            _preferredScale = ClampScale(value);
            ApplyScale(_preferredScale ?? ScaleForDisplay());
        }
    }

    /// <summary>Where the character's frame is on screen, in physical pixels.</summary>
    public (int X, int Y, int Width, int Height) FrameBounds =>
        (_window.X, _window.Y, (Character?.Width ?? 0) * _scale, (Character?.Height ?? 0) * _scale);

    /// <summary>Default pixel size for a monitor DPI: round(DPI / 96), from 1x to 4x.</summary>
    public static int ScaleForDpi(uint dpi) => Math.Clamp((int)Math.Round(dpi / 96.0, MidpointRounding.AwayFromZero), 1, 4);

    public void Load(AcsCharacter character)
    {
        Unload();
        Character = character;
        _sounds = new SoundPlayer(character, _platform.CreateAudioOutput()) { Enabled = SoundsEnabled };
        _player = new AnimationPlayer(character, frame => Present(frame, _window.X, _window.Y), _sounds.Play);
    }

    public void Unload()
    {
        _idleTimer.Stop();
        _player?.Abort();
        _player?.Dispose();
        _sounds?.Dispose();
        _player = null;
        _sounds = null;
        _lastFrame = null;
        _window.Hide();
        Character = null;
    }

    /// <summary>Shows the character with its show animation, then calls <paramref name="shown"/>.</summary>
    public void Show(Action? shown)
    {
        if (Character is null || _player is null)
        {
            return;
        }

        if (!_positioned)
        {
            var work = _platform.Displays.WorkAreaAt(0, 0);
            _scale = _preferredScale ?? ScaleForDpi(_platform.Displays.DpiAt(work.Right - 1, work.Bottom - 1));
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
        PlayInternal(showAnimation, shown);
    }

    /// <summary>Plays the hide animation, then hides the window.</summary>
    public void Hide()
    {
        if (!_window.IsVisible || Character is null)
        {
            return;
        }

        _idleTimer.Stop();
        _hiding = true;
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
        var hideAnimation = Character.GetStateAnimations("HIDING").FirstOrDefault() ?? "Hide";
        if (!PlayInternal(hideAnimation, HideNow))
        {
            HideNow();
        }
    }

    /// <summary>Plays a named animation on the visible character.</summary>
    public bool Play(string animation)
    {
        if (!IsShown)
        {
            return false;
        }

        MarkActive();
        return PlayInternal(animation, null);
    }

    /// <summary>The context menu's "Animate!": a random gesture.</summary>
    public void Animate()
    {
        if (Character is null)
        {
            return;
        }

        var candidates = CharacterAnimations.Gestures(Character).ToList();
        if (candidates.Count == 0)
        {
            candidates = [.. Character.Animations.Select(a => a.Name)];
        }

        Play(candidates[_random.Next(candidates.Count)]);
    }

    /// <summary>The user did something: idling starts over from its calmest level.</summary>
    public void MarkActive() => _idleSince = DateTime.UtcNow;

    /// <summary>A random one of the character's greetings, if it has any.</summary>
    public string? PickGreeting() =>
        Strings?.Greetings is { Count: > 0 } greetings ? greetings[_random.Next(greetings.Count)] : null;

    public void Dispose()
    {
        Unload();
        _idleTimer.Dispose();
        _interaction.Dispose();
    }

    private static int? ClampScale(int? scale) => scale is { } value ? Math.Clamp(value, 1, 4) : null;

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
        if (_player.IsPlaying)
        {
            // Cut the running animation short, including its sound.
            _sounds?.Stop();
        }

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

        var (min, max) = CharacterAnimations.IdleDelay(Character);
        _idleTimer.Start(_random.Next(min, max), PlayIdle);
    }

    private void PlayIdle()
    {
        if (Character is null || !IsShown)
        {
            return;
        }

        var animations = CharacterAnimations.Idle(Character, DateTime.UtcNow - _idleSince);
        if (animations.Count == 0)
        {
            return;
        }

        PlayInternal(animations[_random.Next(animations.Count)], null);
    }

    /// <summary>
    /// Scale for the monitor under the character's bottom-left corner. That corner stays put
    /// when the scale changes, so the choice can't flip back and forth at a monitor edge.
    /// </summary>
    private int ScaleForDisplay() =>
        ScaleForDpi(_platform.Displays.DpiAt(_window.X, _window.Y + Math.Max(0, _window.Height - 1)));

    private void ApplyScale(int value)
    {
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
            BoundsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnMoved()
    {
        // Dragged to a monitor with a different scaling: follow it unless the user picked a size.
        if (_preferredScale is null && _positioned)
        {
            ApplyScale(ScaleForDisplay());
        }

        BoundsChanged?.Invoke(this, EventArgs.Empty);
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
}
