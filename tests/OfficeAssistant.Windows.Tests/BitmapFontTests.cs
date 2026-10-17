using OfficeAssistant.Core.Platform;
using OfficeAssistant.Core.Rendering;
using OfficeAssistant.Platform.Windows;

namespace OfficeAssistant.Windows.Tests;

/// <summary>
/// The portable bitmap font must draw exactly what GDI draws, so the balloon looks the same
/// on every platform. If these fail, regenerate it with <c>AcsTool bake-font</c>.
/// </summary>
public class BitmapFontTests
{
    [WindowsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryBakedGlyphMatchesGdi(bool bold)
    {
        using var gdi = new GdiTextRenderer(BalloonRenderer.FontFace, BalloonRenderer.FontPixelHeight);
        using var bitmap = new BitmapFontRenderer();
        var font = bold ? BalloonFont.Bold : BalloonFont.Regular;
        var mismatches = new List<string>();
        foreach (var c in font.Characters.Order())
        {
            var text = c.ToString();
            if (gdi.Measure(text, bold) != bitmap.Measure(text, bold) || !SamePixels(gdi, bitmap, text, bold))
            {
                mismatches.Add($"U+{(int)c:X4}");
            }
        }

        Assert.Empty(mismatches);
    }

    [WindowsTheory]
    [InlineData("Do you want to save the changes to Document1?", false)]
    [InlineData("What would you like to do?", true)]
    [InlineData("Ærøskøbing, Ŝtiparo, Ελληνικά, Кириллица — “quotes” …", false)]
    public void StringsMatchGdi(string text, bool bold)
    {
        using var gdi = new GdiTextRenderer(BalloonRenderer.FontFace, BalloonRenderer.FontPixelHeight);
        using var bitmap = new BitmapFontRenderer();

        Assert.Equal(gdi.Measure(text, bold), bitmap.Measure(text, bold));
        Assert.True(SamePixels(gdi, bitmap, text, bold));
    }

    [WindowsTheory]
    [InlineData("&Yes")]
    [InlineData("Save &As...")]
    [InlineData("Fish && &Chips")]
    public void MnemonicsMatchGdi(string text)
    {
        using var gdi = new GdiTextRenderer(BalloonRenderer.FontFace, BalloonRenderer.FontPixelHeight);
        using var bitmap = new BitmapFontRenderer();
        var expected = new PixelBuffer(200, 24);
        var actual = new PixelBuffer(200, 24);

        gdi.DrawWithMnemonic(expected, text, 4, 2, 0xFF000000);
        bitmap.DrawWithMnemonic(actual, text, 4, 2, 0xFF000000);

        Assert.Equal(expected.Pixels, actual.Pixels);
    }

    private static bool SamePixels(ITextRenderer expected, ITextRenderer actual, string text, bool bold)
    {
        var width = expected.Measure(text, bold) + 16;
        var a = new PixelBuffer(width, 24);
        var b = new PixelBuffer(width, 24);
        expected.Draw(a, text, 4, 4, 0xFF000000, bold);
        actual.Draw(b, text, 4, 4, 0xFF000000, bold);
        return a.Pixels.AsSpan().SequenceEqual(b.Pixels);
    }
}
