using System.Runtime.InteropServices;
using AcsFormat;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;

namespace OfficeAssistant.Core.Animation;

/// <summary>
/// Plays a character's embedded WAVE clips with PlaySound. Clips are copied to unmanaged
/// memory once because asynchronous in-memory playback reads the buffer after the call returns.
/// </summary>
public sealed unsafe class SoundPlayer(AcsCharacter character) : IDisposable
{
    private readonly Dictionary<int, nint> _buffers = [];

    public bool Enabled { get; set; } = true;

    public void Play(int index)
    {
        if (!Enabled || index < 0 || index >= character.SoundCount)
        {
            return;
        }

        if (!_buffers.TryGetValue(index, out var buffer))
        {
            var data = character.GetSound(index).WaveData.Span;
            buffer = Marshal.AllocHGlobal(data.Length);
            data.CopyTo(new Span<byte>((void*)buffer, data.Length));

            _buffers[index] = buffer;
        }

        // With SND_MEMORY the sound "name" is a pointer to the WAVE data.
        PInvoke.PlaySound((PCWSTR)(char*)buffer, default, SND_FLAGS.SND_MEMORY | SND_FLAGS.SND_ASYNC | SND_FLAGS.SND_NODEFAULT);
    }

    /// <summary>Stops the clip that is playing, if any.</summary>
    public void Stop() => PInvoke.PlaySound(default(PCWSTR), default, 0);

    public void Dispose()
    {
        Stop();
        foreach (var buffer in _buffers.Values)
        {
            Marshal.FreeHGlobal(buffer);
        }

        _buffers.Clear();
    }
}
