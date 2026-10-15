using System.Buffers.Binary;
using System.IO.Compression;

namespace AcsTool;

/// <summary>Minimal RGBA PNG encoder for premultiplied 0xAARRGGBB pixels with 1-bit alpha.</summary>
internal static class PngWriter
{
    public static void Write(string path, int width, int height, ReadOnlySpan<uint> pixels)
    {
        var raw = new byte[height * ((width * 4) + 1)];
        var i = 0;
        for (var y = 0; y < height; y++)
        {
            raw[i++] = 0; // Filter: none.
            for (var x = 0; x < width; x++)
            {
                var p = pixels[(y * width) + x];
                raw[i++] = (byte)(p >> 16);
                raw[i++] = (byte)(p >> 8);
                raw[i++] = (byte)p;
                raw[i++] = (byte)(p >> 24);
            }
        }

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(raw);
        }

        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // Bit depth.
        header[9] = 6; // RGBA.
        WriteChunk(file, "IHDR", header);
        WriteChunk(file, "IDAT", compressed.ToArray());
        WriteChunk(file, "IEND", []);
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        var crc = Crc32(Crc32(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint Crc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc;
    }
}
