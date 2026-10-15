using System.Runtime.InteropServices;
using AcsFormat;
using OfficeAssistant.Core.Interop;

namespace OfficeAssistant.Core.Animation;

/// <summary>
/// Plays a character's embedded WAVE clips with PlaySound. Clips are copied to unmanaged
/// memory once because asynchronous in-memory playback reads the buffer after the call returns.
/// </summary>
public sealed class SoundPlayer(AcsCharacter character) : IDisposable
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
            unsafe
            {
                data.CopyTo(new Span<byte>((void*)buffer, data.Length));
            }

            _buffers[index] = buffer;
        }

        Native.PlaySound(buffer, 0, Native.SND_MEMORY | Native.SND_ASYNC | Native.SND_NODEFAULT);
    }

    public void Dispose()
    {
        Native.PlaySound(0, 0, 0);
        foreach (var buffer in _buffers.Values)
        {
            Marshal.FreeHGlobal(buffer);
        }

        _buffers.Clear();
    }
}
