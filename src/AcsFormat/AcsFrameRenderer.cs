namespace AcsFormat;

/// <summary>
/// Composites animation frames into 32-bit pixel buffers, exactly as stored: no
/// filtering, scaling or color conversion. Output pixels are premultiplied
/// 0xAARRGGBB values (BGRA byte order in memory); transparent pixels are zero.
/// </summary>
public sealed class AcsFrameRenderer(AcsCharacter character)
{
    public AcsCharacter Character { get; } = character;

    public uint[] Render(AcsFrame frame)
    {
        var pixels = new uint[Character.Width * Character.Height];
        Render(frame, pixels);
        return pixels;
    }

    public void Render(AcsFrame frame, Span<uint> destination)
    {
        destination.Clear();

        // The first image in a frame is the top-most, so paint from the back.
        for (var i = frame.Images.Count - 1; i >= 0; i--)
        {
            var placement = frame.Images[i];
            Draw(Character.GetImage(placement.ImageIndex), placement.X, placement.Y, destination);
        }
    }

    private void Draw(AcsImage image, int left, int top, Span<uint> destination)
    {
        var width = Character.Width;
        var height = Character.Height;
        var transparent = Character.TransparentIndex;
        var palette = Character.Palette;

        for (var y = 0; y < image.Height; y++)
        {
            var dy = top + y;
            if ((uint)dy >= (uint)height)
            {
                continue;
            }

            for (var x = 0; x < image.Width; x++)
            {
                var dx = left + x;
                if ((uint)dx >= (uint)width)
                {
                    continue;
                }

                var index = image.Pixels[(y * image.Width) + x];
                if (index != transparent)
                {
                    destination[(dy * width) + dx] = palette[index];
                }
            }
        }
    }
}
