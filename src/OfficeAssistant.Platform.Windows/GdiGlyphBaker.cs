using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace OfficeAssistant.Platform.Windows;

/// <summary>
/// Renders single characters with GDI, non-antialiased, and returns their bitmaps and advance
/// widths: the source data for the portable bitmap font (<c>AcsTool bake-font</c>).
/// </summary>
public static unsafe class GdiGlyphBaker
{
    // Room around a glyph for overhangs, accents above the cell and descenders below it.
    private const int Pad = 16;

    /// <param name="Code">The character.</param>
    /// <param name="Advance">How far the pen moves after it.</param>
    /// <param name="Left">Offset of the bitmap from the pen position.</param>
    /// <param name="Top">Offset of the bitmap's first row from the top of the cell.</param>
    /// <param name="Width">Bitmap width in pixels (0 for blank glyphs).</param>
    /// <param name="Rows">One mask per row, the leftmost pixel in the highest of <paramref name="Width"/> bits.</param>
    public sealed record BakedGlyph(int Code, int Advance, int Left, int Top, int Width, uint[] Rows);

    /// <summary>Bakes every character of <paramref name="codes"/> that the font itself has (no font linking).</summary>
    public static (int Ascent, IReadOnlyList<BakedGlyph> Glyphs) Bake(string face, int pixelHeight, bool bold, IEnumerable<int> codes)
    {
        using var text = new GdiTextRenderer(face, pixelHeight);
        using var canvas = new GdiCanvas(64 + (2 * Pad), pixelHeight + (2 * Pad) + 8);
        text.SelectFont(canvas.Dc, bold);
        PInvoke.SetTextColor(canvas.Dc, default);

        PInvoke.GetTextMetrics(canvas.Dc, out var metrics);
        var glyphs = new List<BakedGlyph>();
        foreach (var code in codes)
        {
            var character = ((char)code).ToString();
            ushort index;
            fixed (char* chars = character)
            {
                PInvoke.GetGlyphIndices(canvas.Dc, chars, 1, &index, PInvoke.GGI_MARK_NONEXISTING_GLYPHS);
            }

            if (index == 0xFFFF)
            {
                continue;
            }

            canvas.Pixels.Fill(0xFF000000);
            PInvoke.TextOut(canvas.Dc, Pad, Pad, character, 1);
            glyphs.Add(Crop(canvas, code, text.Measure(character, bold)));
        }

        return (metrics.tmAscent, glyphs);
    }

    private static BakedGlyph Crop(GdiCanvas canvas, int code, int advance)
    {
        // GDI clears the alpha byte of the pixels it draws.
        var pixels = canvas.Pixels;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                if ((pixels[(y * canvas.Width) + x] >> 24) == 0)
                {
                    (minX, minY) = (Math.Min(minX, x), Math.Min(minY, y));
                    (maxX, maxY) = (Math.Max(maxX, x), Math.Max(maxY, y));
                }
            }
        }

        if (maxX < 0)
        {
            return new BakedGlyph(code, advance, 0, 0, 0, []);
        }

        var width = maxX - minX + 1;
        var rows = new uint[maxY - minY + 1];
        var digits = (width + 3) / 4;
        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if ((pixels[((minY + y) * canvas.Width) + minX + x] >> 24) == 0)
                {
                    rows[y] |= 1u << ((digits * 4) - 1 - x);
                }
            }
        }

        return new BakedGlyph(code, advance, minX - Pad, minY - Pad, width, rows);
    }
}
