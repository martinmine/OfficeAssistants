using OfficeAssistant.Core.Platform;

namespace OfficeAssistant.Core.Rendering;

/// <summary>
/// Balloon text from the pre-rendered Tahoma in <see cref="BalloonFont"/>, identical on
/// every platform to what GDI draws on Windows. Runs of characters the font doesn't have
/// (CJK, scripts that need shaping) go as a whole to a fallback renderer, the platform's
/// own text, when one is given; otherwise each draws nothing and takes the width of a space.
/// </summary>
public sealed class BitmapFontRenderer(ITextRenderer? fallback = null) : ITextRenderer
{
    public int Measure(string text, bool bold)
    {
        var font = bold ? BalloonFont.Bold : BalloonFont.Regular;
        var width = 0;
        for (var i = 0; i < text.Length;)
        {
            var run = MissingRunLength(font, text, i);
            width += run > 0 ? MeasureMissing(font, text.Substring(i, run), bold) : Glyph(font, text[i]).Advance;
            i += Math.Max(1, run);
        }

        return width;
    }

    public void Draw(PixelBuffer target, string text, int x, int y, uint color, bool bold)
    {
        var font = bold ? BalloonFont.Bold : BalloonFont.Regular;
        var opaque = color | 0xFF000000;
        var pen = x;
        for (var i = 0; i < text.Length;)
        {
            var run = MissingRunLength(font, text, i);
            if (run > 0)
            {
                var missing = text.Substring(i, run);
                fallback?.Draw(target, missing, pen, y, opaque, bold);
                pen += MeasureMissing(font, missing, bold);
                i += run;
            }
            else
            {
                pen += DrawGlyph(target, Glyph(font, text[i]), pen, y, opaque);
                i++;
            }
        }
    }

    public void DrawWithMnemonic(PixelBuffer target, string text, int x, int y, uint color)
    {
        var font = BalloonFont.Regular;
        var opaque = color | 0xFF000000;
        var pen = x;
        for (var i = 0; i < text.Length; i++)
        {
            var underline = false;
            if (text[i] == '&' && i + 1 < text.Length)
            {
                i++;
                underline = text[i] != '&';
            }

            var advance = font.TryGetGlyph(text[i], out var glyph)
                ? DrawGlyph(target, glyph, pen, y, opaque)
                : DrawMissing(target, font, text[i].ToString(), pen, y, opaque);
            if (underline)
            {
                // As DrawText does: a line one pixel below the baseline, as wide as the character.
                for (var u = pen; u < pen + advance; u++)
                {
                    target.SetPixel(u, y + font.Ascent + 1, opaque);
                }
            }

            pen += advance;
        }
    }

    public void Dispose() => fallback?.Dispose();

    private static BitmapFont.Glyph Glyph(BitmapFont font, char c) => font.TryGetGlyph(c, out var glyph) ? glyph : throw new ArgumentException($"No glyph for U+{(int)c:X4}.");

    /// <summary>How many characters from <paramref name="start"/> the font doesn't have.</summary>
    private static int MissingRunLength(BitmapFont font, string text, int start)
    {
        var end = start;
        while (end < text.Length && !font.TryGetGlyph(text[end], out _))
        {
            end++;
        }

        return end - start;
    }

    private static int DrawGlyph(PixelBuffer target, BitmapFont.Glyph glyph, int pen, int y, uint color)
    {
        for (var row = 0; row < glyph.Rows.Length; row++)
        {
            var mask = glyph.Rows[row];
            for (var column = 0; mask != 0; column++, mask <<= 1)
            {
                if ((mask & 0x80000000) != 0)
                {
                    target.SetPixel(pen + glyph.Left + column, y + glyph.Top + row, color);
                }
            }
        }

        return glyph.Advance;
    }

    private int MeasureMissing(BitmapFont font, string text, bool bold) =>
        fallback?.Measure(text, bold) ?? (text.Length * (font.TryGetGlyph(' ', out var space) ? space.Advance : 0));

    private int DrawMissing(PixelBuffer target, BitmapFont font, string text, int pen, int y, uint color)
    {
        fallback?.Draw(target, text, pen, y, color, bold: false);
        return MeasureMissing(font, text, bold: false);
    }
}
