namespace AcsFormat;

/// <summary>
/// A decoded 8-bit palettized image. <see cref="Pixels"/> holds one palette index per
/// pixel, top-down, with no row padding.
/// </summary>
public sealed class AcsImage
{
    public AcsImage(int width, int height, byte[] pixels)
    {
        if (pixels.Length != width * height)
        {
            throw new ArgumentException("Pixel buffer does not match the image size.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    public byte this[int x, int y] => Pixels[(y * Width) + x];

    /// <summary>
    /// Decodes an IMAGEINFO block: a stored bottom-up DIB whose rows are padded to
    /// four bytes, optionally compressed. The trailing region data is not needed
    /// because transparency comes from the character's transparent palette index.
    /// </summary>
    internal static AcsImage Decode(ReadOnlySpan<byte> file, int offset)
    {
        var reader = new AcsBinaryReader(file, offset);
        reader.ReadByte(); // Always 1.
        int width = reader.ReadUInt16();
        int height = reader.ReadUInt16();
        var compressed = reader.ReadBool();
        var dataSize = checked((int)reader.ReadUInt32());
        var data = reader.ReadBytes(dataSize);

        var stride = (width + 3) & ~3;
        var dib = compressed ? AcsDecompressor.Decompress(data, stride * height) : data.ToArray();
        if (dib.Length < stride * height)
        {
            throw new InvalidDataException("Image data is shorter than its declared size.");
        }

        var pixels = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            dib.AsSpan((height - 1 - y) * stride, width).CopyTo(pixels.AsSpan(y * width, width));
        }

        return new AcsImage(width, height, pixels);
    }
}
