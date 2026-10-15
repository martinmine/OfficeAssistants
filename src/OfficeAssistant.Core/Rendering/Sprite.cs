namespace OfficeAssistant.Core.Rendering;

/// <summary>A small 0xAARRGGBB bitmap with 1-bit alpha (0 = transparent).</summary>
internal sealed class Sprite(int width, int height, uint[] pixels)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public uint[] Pixels { get; } = pixels;
}
