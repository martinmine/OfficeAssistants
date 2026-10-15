using OfficeAssistant.Core.Interop;

namespace OfficeAssistant.Core.Rendering;

/// <summary>Which side of the balloon the tail is on.</summary>
public enum BalloonTailSide
{
    /// <summary>Balloon above the character, tail pointing down.</summary>
    Bottom,

    /// <summary>Balloon below the character, tail pointing up.</summary>
    Top,
}

/// <summary>Interactive state that changes how a balloon is painted.</summary>
public sealed class BalloonState
{
    public string TextBoxText { get; set; } = string.Empty;

    public bool TextBoxSelected { get; set; }

    public bool TextBoxFocused { get; set; }

    public bool CaretVisible { get; set; }
}

public enum BalloonHitKind
{
    None,
    Label,
    Button,
    TextBox,
}

public readonly record struct BalloonHit(BalloonHitKind Kind, string? Id)
{
    public static readonly BalloonHit None = new(BalloonHitKind.None, null);
}

/// <summary>A painted balloon: pixels for the box plus tail, and where things are.</summary>
public sealed class BalloonBitmap
{
    internal BalloonBitmap(int width, int height, uint[] pixels, int boxTop, int tailTipX, int tailTipY, List<(Native.RECT Rect, BalloonHit Hit)> hits)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
        BoxTop = boxTop;
        TailTipX = tailTipX;
        TailTipY = tailTipY;
        Hits = hits;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Premultiplied 0xAARRGGBB pixels; zero outside the balloon shape.</summary>
    public uint[] Pixels { get; }

    /// <summary>Offset of the rounded box inside the bitmap (non-zero when the tail is on top).</summary>
    public int BoxTop { get; }

    public int TailTipX { get; }

    public int TailTipY { get; }

    internal List<(Native.RECT Rect, BalloonHit Hit)> Hits { get; }

    public BalloonHit HitTest(int x, int y)
    {
        foreach (var (rect, hit) in Hits)
        {
            if (x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom)
            {
                return hit;
            }
        }

        return BalloonHit.None;
    }
}

/// <summary>
/// Draws Office XP assistant balloons. The metrics below were measured from real
/// Office XP screenshots so the output matches them pixel for pixel: Tahoma at
/// 13 px without anti-aliasing, a #FFFFCC box with a one-pixel black border, rounded
/// corners and a 15 pixel tail.
/// </summary>
public sealed class BalloonRenderer : IDisposable
{
    public const int TailHeight = 15;

    /// <summary>Width of the tail opening in the border.</summary>
    public const int TailOpening = 8;

    private const uint Background = 0xFFFFFFCC;
    private const uint Border = 0xFF000000;
    private const uint LabelColor = 0xFF000080;
    private const uint TextBoxBorder = 0xFFECE9D8;
    private const uint TextBoxBackground = 0xFFFFFFFF;
    private const uint SelectionBackground = 0xFF316AC5;
    private const uint SeparatorColor = 0xFFB2B2B2;
    private const uint ButtonBorder = 0xFFC0C0C0;

    private const int LineHeight = 16;
    private const int Padding = 5;
    private const int MinimumWidth = 184;
    private const int MaximumWidth = 300;
    private const int LabelIndent = 27;
    private const int LabelRightPadding = 8;
    private const int TextBoxHeight = 40;
    private const int ButtonHeight = 22;
    private const int ButtonMinimumWidth = 60;
    private const int ButtonTextPadding = 22;
    private const int ButtonGap = 12;
    private const int IconSize = 32;

    // Rows 1..7 of the rounded corner: first and last border pixel, counted from the
    // outer edge. Row 0 is the straight edge starting at column 7; rows from 8 on are
    // a single border pixel at column 0.
    private static readonly (int First, int Last)[] CornerRows =
        [(0, 0), (4, 7), (3, 4), (2, 3), (1, 2), (1, 1), (1, 1), (0, 1)];

    // Left-edge border pixels of the tail for each row below the box, relative to the
    // start of the opening. The right edge is a straight line at TailOpening.
    private static readonly int[][] TailLeftEdge =
    [
        [-1, 0], [0, 1], [1], [1, 2], [2, 3], [3], [3, 4], [4], [4, 5], [5, 6], [6], [6, 7], [7], [], [],
    ];

    private readonly GdiCanvas _measure = new(1, 1);
    private readonly nint _regularFont;
    private readonly nint _boldFont;

    public BalloonRenderer(string fontFace = "Tahoma", int fontHeight = -13)
    {
        _regularFont = CreateFont(fontFace, fontHeight, 400);
        _boldFont = CreateFont(fontFace, fontHeight, 700);
    }

    public void Dispose()
    {
        _measure.Dispose();
        Native.DeleteObject(_regularFont);
        Native.DeleteObject(_boldFont);
    }

    /// <summary>Lays out the content and returns the box size (excluding the tail).</summary>
    public BalloonLayout Measure(BalloonContent content, BalloonState state)
    {
        var width = ComputeWidth(content);
        var blocks = new List<PlacedBlock>();
        var y = Padding;
        BalloonBlock? previous = null;

        foreach (var block in content.Blocks)
        {
            y += block.SpaceBefore ?? DefaultSpacing(previous, block);
            var placed = new PlacedBlock(block, y);

            switch (block)
            {
                case BalloonText text:
                    placed.Lines = Wrap(text.Text, width - (2 * Padding), text.Bold ? _boldFont : _regularFont);
                    placed.Height = placed.Lines.Count * LineHeight;
                    break;
                case BalloonIconHeading:
                    placed.Height = IconSize;
                    break;
                case BalloonLabel label:
                    placed.Lines = Wrap(label.Text, width - LabelIndent - LabelRightPadding, _regularFont);
                    placed.Height = placed.Lines.Count * LineHeight;
                    break;
                case BalloonSeeMore:
                    placed.Height = LineHeight;
                    break;
                case BalloonTextBox:
                    placed.Height = TextBoxHeight;
                    break;
                case BalloonSeparator:
                    placed.Height = 1;
                    break;
                case BalloonButtons buttons:
                    placed.Height = ButtonHeight;
                    placed.ButtonRects = LayoutButtons(buttons, width, y);
                    break;
            }

            blocks.Add(placed);
            y += placed.Height;
            previous = block;
        }

        return new BalloonLayout(content, width, y + Padding + 1, blocks);
    }

    /// <summary>Paints a measured balloon with its tail opening at <paramref name="tailX"/>.</summary>
    public BalloonBitmap Render(BalloonLayout layout, BalloonState state, BalloonTailSide tailSide, int tailX)
    {
        var width = layout.Width;
        var boxHeight = layout.Height;
        var boxTop = tailSide == BalloonTailSide.Top ? TailHeight : 0;
        var height = boxHeight + TailHeight;
        tailX = Math.Clamp(tailX, 9, width - 10 - TailOpening);

        using var canvas = new GdiCanvas(width, height);
        var inside = new bool[width * height];
        var pixels = canvas.Pixels;
        pixels.Clear();

        PaintShape(pixels, inside, width, boxHeight, boxTop, tailSide, tailX);

        var hits = new List<(Native.RECT, BalloonHit)>();
        foreach (var placed in layout.Blocks)
        {
            PaintBlock(canvas, placed, layout.Width, boxTop, state, hits);
        }

        // GDI clears the alpha byte of every pixel it touches; the whole shape is opaque.
        pixels = canvas.Pixels;
        var result = new uint[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            result[i] = inside[i] ? pixels[i] | 0xFF000000 : 0;
        }

        var tipY = tailSide == BalloonTailSide.Bottom ? height - 1 : 0;
        return new BalloonBitmap(width, height, result, boxTop, tailX + TailOpening, tipY, hits);
    }

    private static nint CreateFont(string face, int height, int weight) =>
        Native.CreateFont(height, 0, 0, 0, weight, 0, 0, 0, Native.DEFAULT_CHARSET, 0, 0, Native.NONANTIALIASED_QUALITY, 0, face);

    private static int DefaultSpacing(BalloonBlock? previous, BalloonBlock block) => (previous, block) switch
    {
        (null, _) => 0,
        (_, BalloonLabel or BalloonSeeMore or BalloonSeparator) => 8,
        (BalloonIconHeading, _) => 8,
        (BalloonSeparator, BalloonButtons) => 8,
        (_, BalloonButtons) => 9,
        _ => 16,
    };

    private int ComputeWidth(BalloonContent content)
    {
        var width = MinimumWidth;
        var hasBodyText = content.Blocks.Any(b => b is BalloonText { Bold: false });

        foreach (var block in content.Blocks)
        {
            switch (block)
            {
                case BalloonText { Bold: true } heading when !hasBodyText:
                    width = Math.Max(width, LongestLine(heading.Text, _boldFont) + (2 * Padding));
                    break;
                case BalloonIconHeading heading:
                    width = Math.Max(width, Padding + IconSize + Padding + Extent(heading.Text, _boldFont) + Padding);
                    break;
                case BalloonLabel label:
                    width = Math.Max(width, LabelIndent + LongestLine(label.Text, _regularFont) + LabelRightPadding);
                    break;
                case BalloonButtons buttons:
                    width = Math.Max(width, ButtonGroupWidth(buttons) + (2 * 6));
                    break;
            }
        }

        return Math.Min(width, MaximumWidth);
    }

    private int ButtonWidth(BalloonButtons buttons) =>
        buttons.Buttons.Max(b => Math.Max(ButtonMinimumWidth, Extent(StripMnemonic(b.Text), _regularFont) + ButtonTextPadding));

    private int ButtonGroupWidth(BalloonButtons buttons)
    {
        var count = buttons.Buttons.Count;
        return (count * ButtonWidth(buttons)) + ((count - 1) * ButtonGap);
    }

    private Native.RECT[] LayoutButtons(BalloonButtons buttons, int width, int y)
    {
        var count = buttons.Buttons.Count;
        var buttonWidth = ButtonWidth(buttons);
        var rects = new Native.RECT[count];
        if (count == 1)
        {
            var left = (width - buttonWidth) / 2;
            rects[0] = new Native.RECT(left, y, left + buttonWidth, y + ButtonHeight);
            return rects;
        }

        // Buttons spread over at least the minimum content width, centred in the box.
        var span = Math.Max(MinimumWidth - 12, ButtonGroupWidth(buttons));
        var start = (width - span) / 2;
        for (var i = 0; i < count; i++)
        {
            var left = start + (int)Math.Round(i * (span - buttonWidth) / (double)(count - 1), MidpointRounding.AwayFromZero);
            rects[i] = new Native.RECT(left, y, left + buttonWidth, y + ButtonHeight);
        }

        return rects;
    }

    private static void PaintShape(Span<uint> pixels, bool[] inside, int width, int boxHeight, int boxTop, BalloonTailSide tailSide, int tailX)
    {
        var tailRow = tailSide == BalloonTailSide.Bottom ? boxHeight - 1 : 0;

        for (var row = 0; row < boxHeight; row++)
        {
            var y = boxTop + row;
            var cornerRow = Math.Min(row, boxHeight - 1 - row);

            if (cornerRow == 0)
            {
                for (var x = 7; x < width - 7; x++)
                {
                    var inOpening = row == tailRow && x >= tailX && x < tailX + TailOpening;
                    SetShapePixel(pixels, inside, width, x, y, inOpening ? Background : Border);
                }

                continue;
            }

            int fillStart;
            if (cornerRow < CornerRows.Length)
            {
                var (first, last) = CornerRows[cornerRow];
                for (var x = first; x <= last; x++)
                {
                    SetShapePixel(pixels, inside, width, x, y, Border);
                    SetShapePixel(pixels, inside, width, width - 1 - x, y, Border);
                }

                fillStart = last + 1;
            }
            else
            {
                SetShapePixel(pixels, inside, width, 0, y, Border);
                SetShapePixel(pixels, inside, width, width - 1, y, Border);
                fillStart = 1;
            }

            for (var x = fillStart; x < width - fillStart; x++)
            {
                SetShapePixel(pixels, inside, width, x, y, Background);
            }
        }

        for (var r = 0; r < TailHeight; r++)
        {
            var y = tailSide == BalloonTailSide.Bottom ? boxHeight + r : TailHeight - 1 - r;
            var edge = TailLeftEdge[r];
            var fillStart = edge.Length == 0 ? TailOpening : edge[^1] + 1;
            foreach (var dx in edge)
            {
                SetShapePixel(pixels, inside, width, tailX + dx, y, Border);
            }

            for (var x = fillStart; x < TailOpening; x++)
            {
                SetShapePixel(pixels, inside, width, tailX + x, y, Background);
            }

            SetShapePixel(pixels, inside, width, tailX + TailOpening, y, Border);
        }
    }

        private static void SetShapePixel(Span<uint> pixels, bool[] inside, int width, int x, int y, uint color)
    {
        var i = (y * width) + x;
        pixels[i] = color;
        inside[i] = true;
    }

    private void PaintBlock(GdiCanvas canvas, PlacedBlock placed, int width, int boxTop, BalloonState state, List<(Native.RECT, BalloonHit)> hits)
    {
        var y = boxTop + placed.Y;
        switch (placed.Block)
        {
            case BalloonText text:
                DrawLines(canvas, placed.Lines, Padding, y, text.Bold ? _boldFont : _regularFont, Border);
                break;

            case BalloonIconHeading heading:
                DrawSprite(canvas, BalloonSprites.Warning, Padding, y);
                DrawLines(canvas, [heading.Text], Padding + IconSize + Padding, y + IconSize - LineHeight, _boldFont, Border);
                break;

            case BalloonLabel label:
                DrawSprite(canvas, BalloonSprites.Bullet, 9, y + 4);
                DrawLines(canvas, placed.Lines, LabelIndent, y, _regularFont, LabelColor);
                hits.Add((new Native.RECT(Padding, y, width - Padding, y + placed.Height), new BalloonHit(BalloonHitKind.Label, label.Id)));
                break;

            case BalloonSeeMore seeMore:
                DrawSprite(canvas, BalloonSprites.SeeMoreArrow, 6, y + 5);
                DrawLines(canvas, [seeMore.Text], LabelIndent, y, _regularFont, Border);
                hits.Add((new Native.RECT(Padding, y, width - Padding, y + placed.Height), new BalloonHit(BalloonHitKind.Label, seeMore.Id)));
                break;

            case BalloonTextBox:
                PaintTextBox(canvas, width, y, state);
                hits.Add((new Native.RECT(Padding, y, width - Padding, y + TextBoxHeight), new BalloonHit(BalloonHitKind.TextBox, null)));
                break;

            case BalloonSeparator:
                FillRect(canvas, Padding, y, width - (2 * Padding), 1, SeparatorColor);
                break;

            case BalloonButtons buttons:
                for (var i = 0; i < buttons.Buttons.Count; i++)
                {
                    var rect = placed.ButtonRects[i];
                    rect.Top += boxTop;
                    rect.Bottom += boxTop;
                    PaintButton(canvas, rect, buttons.Buttons[i].Text, i == buttons.FocusedIndex);
                    hits.Add((rect, new BalloonHit(BalloonHitKind.Button, buttons.Buttons[i].Id)));
                }

                break;
        }
    }

    private void PaintTextBox(GdiCanvas canvas, int width, int y, BalloonState state)
    {
        var boxWidth = width - (2 * Padding);
        FillRect(canvas, Padding, y, boxWidth, TextBoxHeight, TextBoxBorder);
        FillRect(canvas, Padding + 1, y + 1, boxWidth - 2, TextBoxHeight - 2, TextBoxBackground);

        var lines = Wrap(state.TextBoxText, boxWidth - 4, _regularFont);
        var maxLines = (TextBoxHeight - 2) / LineHeight;
        if (lines.Count > maxLines)
        {
            lines = lines.Skip(lines.Count - maxLines).ToList();
        }

        var textX = Padding + 2;
        var textY = y + 1;
        for (var i = 0; i < lines.Count; i++)
        {
            var lineY = textY + (i * LineHeight);
            if (state.TextBoxSelected && lines[i].Length > 0)
            {
                FillRect(canvas, textX, lineY, Extent(lines[i], _regularFont), LineHeight, SelectionBackground);
                DrawLines(canvas, [lines[i]], textX, lineY, _regularFont, 0xFFFFFFFF);
            }
            else
            {
                DrawLines(canvas, [lines[i]], textX, lineY, _regularFont, Border);
            }
        }

        if (state.TextBoxFocused && state.CaretVisible && !state.TextBoxSelected)
        {
            var last = lines.Count == 0 ? string.Empty : lines[^1];
            var caretY = textY + (Math.Max(0, lines.Count - 1) * LineHeight);
            FillRect(canvas, textX + Extent(last, _regularFont), caretY, 1, LineHeight, Border);
        }
    }

    private void PaintButton(GdiCanvas canvas, Native.RECT rect, string text, bool focused)
    {
        var w = rect.Right - rect.Left;
        var h = rect.Bottom - rect.Top;
        FillRect(canvas, rect.Left + 2, rect.Top, w - 4, 1, ButtonBorder);
        FillRect(canvas, rect.Left + 2, rect.Bottom - 1, w - 4, 1, ButtonBorder);
        FillRect(canvas, rect.Left, rect.Top + 2, 1, h - 4, ButtonBorder);
        FillRect(canvas, rect.Right - 1, rect.Top + 2, 1, h - 4, ButtonBorder);
        FillRect(canvas, rect.Left + 1, rect.Top + 1, 1, 1, ButtonBorder);
        FillRect(canvas, rect.Right - 2, rect.Top + 1, 1, 1, ButtonBorder);
        FillRect(canvas, rect.Left + 1, rect.Bottom - 2, 1, 1, ButtonBorder);
        FillRect(canvas, rect.Right - 2, rect.Bottom - 2, 1, 1, ButtonBorder);

        var textRect = new Native.RECT(rect.Left, rect.Top + 3, rect.Right, rect.Top + 3 + LineHeight);
        Native.SelectObject(canvas.Dc, _regularFont);
        Native.SetTextColor(canvas.Dc, 0);
        Native.DrawText(canvas.Dc, text, text.Length, ref textRect, Native.DT_SINGLELINE | Native.DT_CENTER);

        if (focused)
        {
            var extent = Extent(StripMnemonic(text), _regularFont);
            var left = rect.Left + ((w - extent) / 2) - 2;
            DrawFocusRect(canvas, left, textRect.Top, extent + 4, LineHeight + 1);
        }
    }

    /// <summary>Same dotted XOR pattern as GDI's DrawFocusRect, on the balloon background.</summary>
    private static void DrawFocusRect(GdiCanvas canvas, int left, int top, int width, int height)
    {
        var pixels = canvas.Pixels;
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                var onEdge = y == top || y == top + height - 1 || x == left || x == left + width - 1;
                if (onEdge && ((x - left) + (y - top)) % 2 == 0 && x >= 0 && y >= 0 && x < canvas.Width && y < canvas.Height)
                {
                    pixels[(y * canvas.Width) + x] ^= 0x00FFFFFF;
                }
            }
        }
    }

    private void DrawLines(GdiCanvas canvas, IReadOnlyList<string> lines, int x, int y, nint font, uint color)
    {
        Native.SelectObject(canvas.Dc, font);
        Native.SetTextColor(canvas.Dc, Native.Rgb(color & 0xFFFFFF));
        for (var i = 0; i < lines.Count; i++)
        {
            Native.TextOut(canvas.Dc, x, y + (i * LineHeight), lines[i], lines[i].Length);
        }
    }

    private static void DrawSprite(GdiCanvas canvas, Sprite sprite, int left, int top)
    {
        var pixels = canvas.Pixels;
        for (var y = 0; y < sprite.Height; y++)
        {
            for (var x = 0; x < sprite.Width; x++)
            {
                var color = sprite.Pixels[(y * sprite.Width) + x];
                var dx = left + x;
                var dy = top + y;
                if (color != 0 && dx >= 0 && dy >= 0 && dx < canvas.Width && dy < canvas.Height)
                {
                    pixels[(dy * canvas.Width) + dx] = color;
                }
            }
        }
    }

    private static void FillRect(GdiCanvas canvas, int left, int top, int width, int height, uint color)
    {
        var pixels = canvas.Pixels;
        for (var y = Math.Max(0, top); y < Math.Min(canvas.Height, top + height); y++)
        {
            for (var x = Math.Max(0, left); x < Math.Min(canvas.Width, left + width); x++)
            {
                pixels[(y * canvas.Width) + x] = color;
            }
        }
    }

    private int Extent(string text, nint font)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        Native.SelectObject(_measure.Dc, font);
        Native.GetTextExtentPoint32(_measure.Dc, text, text.Length, out var size);
        return size.Cx;
    }

    private int LongestLine(string text, nint font) =>
        text.Split('\n').Max(line => Extent(line.TrimEnd(), font));

    /// <summary>Greedy word wrap equivalent to DrawText's DT_WORDBREAK. Wrapped lines keep their trailing space.</summary>
    private List<string> Wrap(string text, int maxWidth, nint font)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = string.Empty;
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Extent(candidate, font) > maxWidth)
                {
                    // Keep the breaking space: selection highlights include it.
                    lines.Add(line + " ");
                    line = word;
                }
                else
                {
                    line = candidate;
                }

                // A single word wider than the line is broken by characters.
                while (line.Length > 1 && Extent(line, font) > maxWidth)
                {
                    var fit = line.Length - 1;
                    while (fit > 1 && Extent(line[..fit], font) > maxWidth)
                    {
                        fit--;
                    }

                    lines.Add(line[..fit]);
                    line = line[fit..];
                }
            }

            lines.Add(line);
        }

        return lines;
    }

    private static string StripMnemonic(string text) => text.Replace("&&", "\0").Replace("&", string.Empty).Replace("\0", "&");
}

/// <summary>Result of <see cref="BalloonRenderer.Measure"/>.</summary>
public sealed class BalloonLayout
{
    internal BalloonLayout(BalloonContent content, int width, int height, List<PlacedBlock> blocks)
    {
        Content = content;
        Width = width;
        Height = height;
        Blocks = blocks;
    }

    public BalloonContent Content { get; }

    /// <summary>Box width including the border.</summary>
    public int Width { get; }

    /// <summary>Box height including the border, excluding the tail.</summary>
    public int Height { get; }

    internal List<PlacedBlock> Blocks { get; }
}

internal sealed class PlacedBlock(BalloonBlock block, int y)
{
    public BalloonBlock Block { get; } = block;

    public int Y { get; } = y;

    public int Height { get; set; }

    public IReadOnlyList<string> Lines { get; set; } = [];

    public Native.RECT[] ButtonRects { get; set; } = [];
}
