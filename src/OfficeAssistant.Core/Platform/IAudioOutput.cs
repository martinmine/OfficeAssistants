namespace OfficeAssistant.Core.Platform;

/// <summary>A sound prepared for playback by an <see cref="IAudioOutput"/>.</summary>
public interface IAudioClip : IDisposable
{
}

/// <summary>Plays one clip at a time. Separate outputs play over each other.</summary>
public interface IAudioOutput : IDisposable
{
    /// <summary>Prepares a RIFF WAVE file, or returns null when it can't be played.</summary>
    IAudioClip? Prepare(ReadOnlySpan<byte> wave);

    /// <summary>Plays a clip prepared by this output, cutting off the one playing.</summary>
    void Play(IAudioClip clip);

    /// <summary>Stops the clip that is playing, if any.</summary>
    void Stop();
}
