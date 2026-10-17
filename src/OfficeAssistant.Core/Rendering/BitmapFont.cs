using System.Globalization;

namespace OfficeAssistant.Core.Rendering;

/// <summary>A pre-rendered monochrome font: one bitmap and advance width per character.</summary>
internal sealed class BitmapFont
{
    private readonly Dictionary<char, Glyph> _glyphs;

    private BitmapFont(int ascent, Dictionary<char, Glyph> glyphs)
    {
        Ascent = ascent;
        _glyphs = glyphs;
    }

    /// <summary>Distance from the top of a line's cell to the baseline.</summary>
    public int Ascent { get; }

    /// <summary>Every character the font has.</summary>
    public IEnumerable<char> Characters => _glyphs.Keys;

    public bool TryGetGlyph(char c, out Glyph glyph) => _glyphs.TryGetValue(c, out glyph!);

    /// <summary>Builds the font from the generated data in <see cref="BalloonFont"/>.</summary>
    public static BitmapFont Decode(int ascent, IReadOnlyList<GlyphData> glyphs)
    {
        var decoded = new Dictionary<char, Glyph>(glyphs.Count);
        foreach (var data in glyphs)
        {
            var digits = (data.Width + 3) / 4;
            var rowCount = digits == 0 ? 0 : data.Rows.Length / digits;
            var rows = new uint[rowCount];
            for (var y = 0; y < rowCount; y++)
            {
                // Shift so the leftmost pixel is bit 31, whatever the glyph's width.
                var value = uint.Parse(data.Rows.AsSpan(y * digits, digits), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                rows[y] = digits == 0 ? 0 : value << (32 - (digits * 4));
            }

            decoded[(char)data.Code] = new Glyph(data.Advance, data.Left, data.Top, data.Width, rows);
        }

        return new BitmapFont(ascent, decoded);
    }

    /// <summary>A glyph as generated: rows are hex digits, ceil(width / 4) per row.</summary>
    internal readonly record struct GlyphData(int Code, int Advance, int Left, int Top, int Width, string Rows);

    /// <param name="Advance">How far the pen moves after the glyph.</param>
    /// <param name="Left">Offset of the bitmap from the pen position.</param>
    /// <param name="Top">Offset of the bitmap's first row from the top of the cell.</param>
    /// <param name="Width">Bitmap width in pixels.</param>
    /// <param name="Rows">One mask per row, the leftmost pixel in bit 31.</param>
    internal sealed record Glyph(int Advance, int Left, int Top, int Width, uint[] Rows);
}
