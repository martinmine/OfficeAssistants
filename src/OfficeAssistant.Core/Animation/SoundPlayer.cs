using AcsFormat;
using OfficeAssistant.Core.Platform;

namespace OfficeAssistant.Core.Animation;

/// <summary>
/// Plays a character's embedded WAVE clips, one at a time: a new clip cuts off the previous
/// one. Each player has its own <see cref="IAudioOutput"/>, so two players (the floating
/// assistant and the gallery preview) play over each other. Clips are prepared once, the
/// first time they play.
/// </summary>
public sealed class SoundPlayer(AcsCharacter character, IAudioOutput output) : IDisposable
{
    private readonly Dictionary<int, IAudioClip?> _clips = [];

    public bool Enabled { get; set; } = true;

    public void Play(int index)
    {
        if (!Enabled || index < 0 || index >= character.SoundCount)
        {
            return;
        }

        Stop();
        if (GetClip(index) is { } clip)
        {
            output.Play(clip);
        }
    }

    /// <summary>Stops the clip that is playing, if any.</summary>
    public void Stop() => output.Stop();

    public void Dispose()
    {
        output.Dispose();
        foreach (var clip in _clips.Values)
        {
            clip?.Dispose();
        }

        _clips.Clear();
    }

    private IAudioClip? GetClip(int index)
    {
        if (!_clips.TryGetValue(index, out var clip))
        {
            clip = output.Prepare(character.GetSound(index).WaveData.Span);
            _clips[index] = clip;
        }

        return clip;
    }
}
