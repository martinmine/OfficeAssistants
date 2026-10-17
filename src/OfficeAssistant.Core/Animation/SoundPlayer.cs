using System.Runtime.InteropServices;
using AcsFormat;
using Windows.Win32;
using Windows.Win32.Media.Audio;

namespace OfficeAssistant.Core.Animation;

/// <summary>
/// Plays a character's embedded WAVE clips, one at a time: a new clip cuts off the previous
/// one. Each player has its own waveOut device handle, so two players (the floating
/// assistant and the gallery preview) play over each other instead of cutting each other
/// off, as they did with PlaySound's single channel per process. Compressed formats are
/// converted by the system (WAVE_MAPPER). Clips are copied to unmanaged memory once,
/// because the device reads them after the call returns.
/// </summary>
public sealed unsafe class SoundPlayer(AcsCharacter character) : IDisposable
{
    private readonly Dictionary<int, Clip?> _clips = [];
    private HWAVEOUT _device;
    private WAVEHDR* _header;

    public bool Enabled { get; set; } = true;

    public void Play(int index)
    {
        if (!Enabled || index < 0 || index >= character.SoundCount)
        {
            return;
        }

        Stop();
        if (GetClip(index) is not { } clip)
        {
            return;
        }

        HWAVEOUT device;
        if (PInvoke.waveOutOpen(&device, PInvoke.WAVE_MAPPER, clip.Format, 0, 0, 0) != 0)
        {
            // No audio device, or a format nothing can convert: stay silent, like PlaySound did.
            return;
        }

        var header = (WAVEHDR*)NativeMemory.AllocZeroed((nuint)sizeof(WAVEHDR));
        header->lpData = clip.Data;
        header->dwBufferLength = clip.DataLength;
        if (PInvoke.waveOutPrepareHeader(device, header, (uint)sizeof(WAVEHDR)) != 0)
        {
            NativeMemory.Free(header);
            PInvoke.waveOutClose(device);
            return;
        }

        _device = device;
        _header = header;
        PInvoke.waveOutWrite(device, header, (uint)sizeof(WAVEHDR));
    }

    /// <summary>Stops the clip that is playing, if any.</summary>
    public void Stop()
    {
        if (_device.IsNull)
        {
            return;
        }

        PInvoke.waveOutReset(_device);
        PInvoke.waveOutUnprepareHeader(_device, _header, (uint)sizeof(WAVEHDR));
        PInvoke.waveOutClose(_device);
        NativeMemory.Free(_header);
        _device = default;
        _header = null;
    }

    public void Dispose()
    {
        Stop();
        foreach (var clip in _clips.Values)
        {
            clip?.Free();
        }

        _clips.Clear();
    }

    private Clip? GetClip(int index)
    {
        if (!_clips.TryGetValue(index, out var clip))
        {
            clip = Clip.Create(character.GetSound(index).WaveData.Span);
            _clips[index] = clip;
        }

        return clip;
    }

    /// <summary>A WAVE file's format and samples in unmanaged memory.</summary>
    private sealed class Clip
    {
        private Clip(WAVEFORMATEX* format, byte* data, uint dataLength)
        {
            Format = format;
            Data = data;
            DataLength = dataLength;
        }

        public WAVEFORMATEX* Format { get; }

        public byte* Data { get; }

        public uint DataLength { get; }

        public static Clip? Create(ReadOnlySpan<byte> wave)
        {
            if (!WaveFile.TryParse(wave, out var formatRange, out var dataRange))
            {
                return null;
            }

            // A PCM "fmt " chunk is 16 bytes; WAVEFORMATEX adds cbSize, which must then be 0.
            var formatBytes = wave[formatRange];
            var formatSize = Math.Max(formatBytes.Length, sizeof(WAVEFORMATEX));
            var format = (byte*)NativeMemory.AllocZeroed((nuint)formatSize);
            formatBytes.CopyTo(new Span<byte>(format, formatSize));

            var samples = wave[dataRange];
            var data = (byte*)NativeMemory.Alloc((nuint)Math.Max(1, samples.Length));
            samples.CopyTo(new Span<byte>(data, samples.Length));
            return new Clip((WAVEFORMATEX*)format, data, (uint)samples.Length);
        }

        public void Free()
        {
            NativeMemory.Free(Format);
            NativeMemory.Free(Data);
        }
    }
}
