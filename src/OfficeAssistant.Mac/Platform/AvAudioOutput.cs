using AVFoundation;
using OfficeAssistant.Core.Platform;

namespace OfficeAssistant.Mac.Platform;

/// <summary>
/// Plays WAVE clips with AVAudioPlayer, which decodes PCM and ADPCM WAVE files itself.
/// Each output has its own players, so outputs play over each other.
/// </summary>
public sealed class AvAudioOutput : IAudioOutput
{
    private AVAudioPlayer? _playing;

    public IAudioClip? Prepare(ReadOnlySpan<byte> wave)
    {
        var player = AVAudioPlayer.FromData(NSData.FromArray(wave.ToArray()), out var error);
        if (player is null || error is not null)
        {
            return null;
        }

        player.PrepareToPlay();
        return new Clip(player);
    }

    public void Play(IAudioClip clip)
    {
        Stop();
        var player = ((Clip)clip).Player;
        player.CurrentTime = 0;
        player.Play();
        _playing = player;
    }

    public void Stop()
    {
        _playing?.Stop();
        _playing = null;
    }

    public void Dispose() => Stop();

    private sealed class Clip(AVAudioPlayer player) : IAudioClip
    {
        public AVAudioPlayer Player { get; } = player;

        public void Dispose() => Player.Dispose();
    }
}
