using CoreText;
using OfficeAssistant.Core.Platform;

namespace OfficeAssistant.Mac.Platform;

/// <summary>
/// Balloon text drawn by Core Text without anti-aliasing. Only used for the characters the
/// baked Tahoma lacks (CJK, scripts that need shaping); Core Text picks fallback fonts and
/// shapes the run itself.
/// </summary>
public sealed class CoreTextRenderer(string face, int pixelHeight) : ITextRenderer
{
    private const int Pad = 8;

    // Tahoma's ascent at 13 px, so fallback text sits on the same baseline as the baked glyphs.
    private readonly int _ascent = (int)Math.Round(pixelHeight * 2049 / 2048.0);
    private readonly CTFont _regular = new(face, pixelHeight);
    private readonly CTFont _bold = new($"{face} Bold", pixelHeight);

    public int Measure(string text, bool bold)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        using var line = CreateLine(text, bold);
        return (int)Math.Ceiling(line.GetTypographicBounds(out _, out _, out _));
    }

    public void Draw(PixelBuffer target, string text, int x, int y, uint color, bool bold)
    {
        if (text.Length == 0)
        {
            return;
        }

        var width = Measure(text, bold) + (2 * Pad);
        var height = pixelHeight + (2 * Pad) + 8;
        var data = new byte[width * height * 4];
        using (var colorSpace = CGColorSpace.CreateSrgb())
        using (var context = new CGBitmapContext(data, width, height, 8, width * 4, colorSpace, CGImageAlphaInfo.PremultipliedLast))
        using (var line = CreateLine(text, bold))
        {
            context.SetShouldAntialias(false);
            context.SetAllowsAntialiasing(false);
            context.SetShouldSmoothFonts(false);
            context.SetFillColor(0, 0, 0, 1);
            context.TextPosition = new CGPoint(Pad, height - Pad - _ascent);
            line.Draw(context);
        }

        var opaque = color | 0xFF000000;
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                if (data[(((row * width) + column) * 4) + 3] != 0)
                {
                    target.SetPixel(x - Pad + column, y - Pad + row, opaque);
                }
            }
        }
    }

    public void DrawWithMnemonic(PixelBuffer target, string text, int x, int y, uint color) =>
        Draw(target, text.Replace("&&", "\0").Replace("&", string.Empty).Replace("\0", "&"), x, y, color, bold: false);

    public void Dispose()
    {
        _regular.Dispose();
        _bold.Dispose();
    }

    private CTLine CreateLine(string text, bool bold)
    {
        var attributes = new CTStringAttributes { Font = bold ? _bold : _regular, ForegroundColorFromContext = true };
        using var attributed = new NSAttributedString(text, attributes);
        return new CTLine(attributed);
    }
}
