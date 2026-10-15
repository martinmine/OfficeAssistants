using AcsFormat;

namespace OfficeAssistant.Core.Animation;

/// <summary>
/// Plays Agent animations frame by frame with the original timing, random branching,
/// sounds and return animations. A new request replaces the running animation at once;
/// <see cref="Stop"/> leaves gracefully through the exit branches.
/// </summary>
public sealed class AnimationPlayer : IDisposable
{
    // Guards against animations made only of zero-length frames looping forever.
    private const int MaxImmediateSteps = 256;

    // An exit path longer than this is treated as broken and cut short.
    private const int MaxExitFrames = 500;

    private readonly AcsCharacter _character;
    private readonly AcsFrameRenderer _renderer;
    private readonly Action<uint[]> _present;
    private readonly Action<int>? _playSound;
    private readonly UiTimer _timer = new();
    private readonly Random _random = new();
    private readonly uint[] _frameBuffer;

    private AcsAnimation? _current;
    private int _frameIndex;
    private bool _exiting;
    private int _exitFrames;
    private Action? _completed;

    public AnimationPlayer(AcsCharacter character, Action<uint[]> present, Action<int>? playSound)
    {
        _character = character;
        _renderer = new AcsFrameRenderer(character);
        _present = present;
        _playSound = playSound;
        _frameBuffer = new uint[character.Width * character.Height];
    }

    public bool IsPlaying => _current is not null;

    public string? CurrentAnimation => _current?.Name;

    /// <summary>The most recently presented frame (premultiplied 0xAARRGGBB).</summary>
    public uint[] CurrentFrame => _frameBuffer;

    /// <summary>
    /// Plays an animation, cancelling any running one immediately (its completion
    /// callback is dropped). Returns false if the name is unknown.
    /// </summary>
    public bool Play(string name, Action? completed = null)
    {
        var animation = _character.FindAnimation(name);
        if (animation is null)
        {
            return false;
        }

        Abort();
        Start(animation, completed);
        return true;
    }

    /// <summary>Asks the running animation to finish through its exit branches.</summary>
    public void Stop()
    {
        if (_current is not null)
        {
            _exiting = true;
        }
    }

    /// <summary>Stops immediately without playing the exit path.</summary>
    public void Abort()
    {
        _timer.Stop();
        _current = null;
        _completed = null;
    }

    /// <summary>Draws the first frame of an animation without playing it.</summary>
    public void ShowStill(string name)
    {
        var animation = _character.FindAnimation(name);
        if (animation is { Frames.Count: > 0 })
        {
            Present(animation.Frames[0]);
        }
    }

    public void Dispose() => _timer.Dispose();

    private void Start(AcsAnimation animation, Action? completed)
    {
        _current = animation;
        _completed = completed;
        _exiting = false;
        _exitFrames = 0;
        _frameIndex = 0;
        Step(0);
    }

    private void Step(int steps)
    {
        while (_current is { } animation)
        {
            if (_frameIndex < 0 || _frameIndex >= animation.Frames.Count)
            {
                Finish();
                return;
            }

            var frame = animation.Frames[_frameIndex];
            if (frame.Duration > 0 || IsLastFrame(animation))
            {
                Present(frame);
            }

            if (frame.HasSound && frame.SoundIndex < _character.SoundCount)
            {
                _playSound?.Invoke(frame.SoundIndex);
            }

            if (frame.Duration > 0)
            {
                _timer.Start(frame.DurationMilliseconds, () =>
                {
                    Advance();
                    Step(0);
                });
                return;
            }

            if (++steps > MaxImmediateSteps)
            {
                Finish();
                return;
            }

            Advance();
        }
    }

    private bool IsLastFrame(AcsAnimation animation) => _frameIndex == animation.Frames.Count - 1;

    private void Advance()
    {
        var animation = _current!;
        var frame = animation.Frames[_frameIndex];

        if (_exiting)
        {
            if (++_exitFrames > MaxExitFrames)
            {
                _frameIndex = animation.Frames.Count;
                return;
            }

            // Exit branches skip straight to the frames that return to the rest pose;
            // negative values mean "continue with the next frame".
            _frameIndex = frame.ExitFrame >= 0 && frame.ExitFrame != _frameIndex ? frame.ExitFrame : _frameIndex + 1;
            return;
        }

        if (frame.Branches.Count > 0)
        {
            var roll = _random.Next(100);
            var cumulative = 0;
            foreach (var branch in frame.Branches)
            {
                cumulative += branch.Probability;
                if (roll < cumulative)
                {
                    _frameIndex = branch.FrameIndex;
                    return;
                }
            }
        }

        _frameIndex++;
    }

    private void Finish()
    {
        var finished = _current;
        var completed = _completed;
        _current = null;
        _completed = null;
        _exiting = false;

        if (finished is { TransitionType: AcsTransitionType.ReturnAnimation, ReturnAnimation.Length: > 0 }
            && _character.FindAnimation(finished.ReturnAnimation) is { } returnAnimation
            && returnAnimation != finished)
        {
            Start(returnAnimation, completed);
            return;
        }

        completed?.Invoke();
    }

    private void Present(AcsFrame frame)
    {
        if (frame.Images.Count == 0)
        {
            return;
        }

        _renderer.Render(frame, _frameBuffer);
        _present(_frameBuffer);
    }
}
