using AcsFormat;
using CoreAnimation;
using OfficeAssistant.Core.Animation;
using OfficeAssistant.Core.Platform;

namespace OfficeAssistant.Mac.Settings;

/// <summary>
/// Plays the gallery assistant's wave in a layer with nearest-neighbour scaling, at the
/// assistant's pixel size, with its sounds unless muted.
/// </summary>
internal sealed class GalleryPreview : NSView
{
    private const int PauseMilliseconds = 1500;

    private readonly IPlatform _platform;
    private readonly UiTimer _replayTimer = new();
    private readonly NSLayoutConstraint _width;
    private readonly NSLayoutConstraint _height;
    private AnimationPlayer? _player;
    private SoundPlayer? _sounds;
    private AcsCharacter? _character;
    private bool _muted;

    public GalleryPreview(IPlatform platform)
    {
        _platform = platform;
        WantsLayer = true;
        Layer!.MagnificationFilter = CALayer.FilterNearest;
        Layer.ContentsGravity = CALayer.GravityResize;
        TranslatesAutoresizingMaskIntoConstraints = false;
        _width = WidthAnchor.ConstraintEqualTo(124);
        _height = HeightAnchor.ConstraintEqualTo(93);
        _width.Active = _height.Active = true;
    }

    /// <summary>Pixel size: each art pixel is this many points.</summary>
    public int Scale { get; set; } = 1;

    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            if (_sounds is not null)
            {
                _sounds.Enabled = !value;
                if (value)
                {
                    _sounds.Stop();
                }
            }
        }
    }

    public void Start(AcsCharacter? character)
    {
        Stop();
        _character = character;
        if (character is null)
        {
            Layer!.Contents = null;
            return;
        }

        _width.Constant = character.Width * Scale;
        _height.Constant = character.Height * Scale;
        _sounds = new SoundPlayer(character, _platform.CreateAudioOutput()) { Enabled = !Muted };
        _player = new AnimationPlayer(character, Show, _sounds.Play);
        Play();
    }

    public void Stop()
    {
        _replayTimer.Stop();
        _player?.Abort();
        _player?.Dispose();
        _sounds?.Dispose();
        _player = null;
        _sounds = null;
    }

    public void UpdateScale(int scale)
    {
        Scale = scale;
        if (_character is not null)
        {
            _width.Constant = _character.Width * scale;
            _height.Constant = _character.Height * scale;
        }
    }

    private void Play()
    {
        // The gallery files hold a single "Wave"; full characters also have one.
        var animation = _character?.FindAnimation("Wave")?.Name ?? _character?.Animations.FirstOrDefault()?.Name;
        if (_player is null || animation is null)
        {
            return;
        }

        _player.Play(animation, () => _replayTimer.Start(PauseMilliseconds, Play));
    }

    private void Show(uint[] frame)
    {
        if (_character is null)
        {
            return;
        }

        var bytes = new byte[frame.Length * 4];
        Buffer.BlockCopy(frame, 0, bytes, 0, bytes.Length);
        using var colorSpace = CGColorSpace.CreateSrgb();
        using var provider = new CGDataProvider(bytes);
        using var image = new CGImage(
            _character.Width, _character.Height, 8, 32, _character.Width * 4, colorSpace,
            CGBitmapFlags.ByteOrder32Little | CGBitmapFlags.PremultipliedFirst,
            provider, null, false, CGColorRenderingIntent.Default);
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        Layer!.Contents = image;
        CATransaction.Commit();
    }
}
