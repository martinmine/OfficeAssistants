namespace OfficeAssistant.Core.Rendering;

/// <summary>A small 0xAARRGGBB bitmap with 1-bit alpha (0 = transparent).</summary>
public sealed class Sprite(int width, int height, uint[] pixels)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    /// <summary>Row-major 0xAARRGGBB pixels; alpha is 0 or 0xFF, so they are also premultiplied.</summary>
    public IReadOnlyList<uint> Pixels { get; } = pixels;
}
