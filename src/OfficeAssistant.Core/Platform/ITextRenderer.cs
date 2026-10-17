namespace OfficeAssistant.Core.Platform;

/// <summary>A 32-bit 0xAARRGGBB pixel buffer, rows top to bottom.</summary>
public sealed class PixelBuffer(int width, int height)
{
    public int Width { get; } = Math.Max(1, width);

    public int Height { get; } = Math.Max(1, height);

    public uint[] Pixels { get; } = new uint[Math.Max(1, width) * Math.Max(1, height)];

    public void SetPixel(int x, int y, uint color)
    {
        if (x >= 0 && y >= 0 && x < Width && y < Height)
        {
            Pixels[(y * Width) + x] = color;
        }
    }
}

/// <summary>
/// Draws single lines of balloon text without anti-aliasing: every pixel is either the
/// text colour or left alone. Widths are the sum of the characters' advances (no kerning).
/// </summary>
public interface ITextRenderer : IDisposable
{
    /// <summary>Width of <paramref name="text"/> in pixels.</summary>
    int Measure(string text, bool bold);

    /// <summary>Draws <paramref name="text"/> with its cell's top-left corner at (<paramref name="x"/>, <paramref name="y"/>), in an opaque <paramref name="color"/>.</summary>
    void Draw(PixelBuffer target, string text, int x, int y, uint color, bool bold);

    /// <summary>
    /// Like <see cref="Draw"/> (regular weight) but '&amp;' underlines the next character
    /// and "&amp;&amp;" is a literal '&amp;', as in menus and buttons.
    /// </summary>
    void DrawWithMnemonic(PixelBuffer target, string text, int x, int y, uint color);
}
