namespace AcsFormat;

/// <summary>
/// Decoder for the LZ77-style bit-stream compression used by Microsoft Agent for
/// images and region data. Bits are consumed least-significant first; multi-bit
/// values are assembled least-significant bit first.
/// </summary>
public static class AcsDecompressor
{
    private const int EndMarker = 0xFFFFF;

    /// <summary>Decompresses <paramref name="source"/> into exactly <paramref name="decompressedSize"/> bytes.</summary>
    /// <exception cref="InvalidDataException">The stream is malformed.</exception>
    public static byte[] Decompress(ReadOnlySpan<byte> source, int decompressedSize)
    {
        var output = new byte[decompressedSize];
        Decompress(source, output);
        return output;
    }

    /// <summary>Decompresses into <paramref name="destination"/>, returning the number of bytes written.</summary>
    public static int Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (source.Length < 7 || source[0] != 0)
        {
            throw new InvalidDataException("Compressed ACS data must start with a zero byte.");
        }

        var bits = new BitReader(source, 8);
        var written = 0;

        while (written < destination.Length)
        {
            if (!bits.ReadBit())
            {
                destination[written++] = (byte)bits.ReadBits(8);
                continue;
            }

            var count = 2;
            int offset;
            if (!bits.ReadBit())
            {
                offset = bits.ReadBits(6) + 1;
            }
            else if (!bits.ReadBit())
            {
                offset = bits.ReadBits(9) + 65;
            }
            else if (!bits.ReadBit())
            {
                offset = bits.ReadBits(12) + 577;
            }
            else
            {
                offset = bits.ReadBits(20);
                if (offset == EndMarker)
                {
                    break;
                }

                offset += 4673;
                count++;
            }

            var lengthBits = 0;
            while (bits.ReadBit())
            {
                if (++lengthBits == 12)
                {
                    throw new InvalidDataException("Invalid run length in compressed ACS data.");
                }
            }

            count += (1 << lengthBits) - 1 + bits.ReadBits(lengthBits);

            if (offset > written)
            {
                throw new InvalidDataException("Back-reference points before the start of the output.");
            }

            // Byte-by-byte copy: runs may overlap their own output.
            for (var i = 0; i < count && written < destination.Length; i++, written++)
            {
                destination[written] = destination[written - offset];
            }
        }

        return written;
    }

    private ref struct BitReader(ReadOnlySpan<byte> data, int startBit)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _bit = startBit;

        public bool ReadBit()
        {
            var index = _bit >> 3;
            if (index >= _data.Length)
            {
                throw new InvalidDataException("Unexpected end of compressed ACS data.");
            }

            var value = (_data[index] >> (_bit & 7)) & 1;
            _bit++;
            return value != 0;
        }

        public int ReadBits(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++)
            {
                if (ReadBit())
                {
                    value |= 1 << i;
                }
            }

            return value;
        }
    }
}
