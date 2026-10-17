using OfficeAssistant.Core.Platform;
using OfficeAssistant.Core.Rendering;

namespace AcsFormat.Tests;

public class BitmapFontRendererTests
{
    [Fact]
    public void WidthsAreTheSumOfTheAdvances()
    {
        using var text = new BitmapFontRenderer();
        Assert.Equal(text.Measure("W", false) + text.Measure("o", false) + text.Measure("rd", false), text.Measure("Word", false));
        Assert.True(text.Measure("Word", true) > text.Measure("Word", false));
        Assert.Equal(0, text.Measure(string.Empty, false));
    }

    [Fact]
    public void MissingCharactersGoToTheFallbackAsWholeRuns()
    {
        var fallback = new RecordingText();
        using var text = new BitmapFontRenderer(fallback);
        var target = new PixelBuffer(300, 20);

        text.Draw(target, "Hi 你好世界 there", 0, 0, 0xFF000000, false);

        Assert.Equal(["你好世界"], fallback.Drawn);
        Assert.Equal(text.Measure("Hi ", false) + (4 * RecordingText.CharWidth) + text.Measure(" there", false), text.Measure("Hi 你好世界 there", false));
    }

    [Fact]
    public void WithoutAFallbackMissingCharactersAreBlankSpaces()
    {
        using var text = new BitmapFontRenderer();
        Assert.Equal(2 * text.Measure(" ", false), text.Measure("你好", false));
    }

    [Fact]
    public void MnemonicsUnderlineTheNextCharacter()
    {
        using var text = new BitmapFontRenderer();
        var plain = new PixelBuffer(60, 20);
        var marked = new PixelBuffer(60, 20);

        text.DrawWithMnemonic(plain, "Yes", 0, 0, 0xFF000000);
        text.DrawWithMnemonic(marked, "&Yes", 0, 0, 0xFF000000);

        // The only difference is the underline on row 14 (one below the baseline), under the Y.
        var differences = Enumerable.Range(0, plain.Pixels.Length).Where(i => plain.Pixels[i] != marked.Pixels[i]).ToList();
        Assert.All(differences, i => Assert.Equal(14, i / 60));
        Assert.Equal(text.Measure("Y", false), differences.Count);
    }

    private sealed class RecordingText : ITextRenderer
    {
        public const int CharWidth = 10;

        public List<string> Drawn { get; } = [];

        public int Measure(string text, bool bold) => text.Length * CharWidth;

        public void Draw(PixelBuffer target, string text, int x, int y, uint color, bool bold) => Drawn.Add(text);

        public void DrawWithMnemonic(PixelBuffer target, string text, int x, int y, uint color) => Drawn.Add(text);

        public void Dispose()
        {
        }
    }
}
