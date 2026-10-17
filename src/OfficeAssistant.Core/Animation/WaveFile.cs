using System.Buffers.Binary;

namespace OfficeAssistant.Core.Animation;

/// <summary>Finds the format and the samples in a RIFF WAVE file.</summary>
internal static class WaveFile
{
    /// <summary>
    /// Locates the "fmt " and "data" chunks. Offsets and lengths are in bytes from the start
    /// of <paramref name="wave"/>; a data chunk that claims more than the file holds is cut short.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> wave, out Range format, out Range data)
    {
        format = default;
        data = default;
        if (wave.Length < 12 || !wave[..4].SequenceEqual("RIFF"u8) || !wave[8..12].SequenceEqual("WAVE"u8))
        {
            return false;
        }

        var foundFormat = false;
        var position = 12;
        while (position + 8 <= wave.Length)
        {
            var id = wave.Slice(position, 4);
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(wave[(position + 4)..]), int.MaxValue);
            var start = position + 8;
            var length = Math.Min(size, wave.Length - start);
            if (id.SequenceEqual("fmt "u8))
            {
                format = start..(start + length);
                foundFormat = length >= 16;
            }
            else if (id.SequenceEqual("data"u8))
            {
                data = start..(start + length);
                return foundFormat;
            }

            // Chunks are padded to an even size.
            var next = (long)start + size + (size & 1);
            if (next > wave.Length)
            {
                break;
            }

            position = (int)next;
        }

        return false;
    }
}
