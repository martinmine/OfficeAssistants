using OfficeAssistant.Core.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace OfficeAssistant.Platform.Windows;

/// <summary>
/// Balloon text drawn by GDI with a non-antialiased font, exactly as Office XP drew it.
/// Text is drawn into a scratch DIB whose pixels start out with alpha 0xFF; GDI clears the
/// alpha byte of every pixel it touches, which tells the text pixels apart from the rest.
/// </summary>
public sealed unsafe class GdiTextRenderer : ITextRenderer
{
    private readonly GdiCanvas _measure = new(1, 1);
    private readonly HFONT _regularFont;
    private readonly HFONT _boldFont;
    private GdiCanvas? _scratch;

    public GdiTextRenderer(string face, int pixelHeight)
    {
        _regularFont = CreateFont(face, -pixelHeight, 400);
        _boldFont = CreateFont(face, -pixelHeight, 700);
    }

    public int Measure(string text, bool bold)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        PInvoke.SelectObject(_measure.Dc, bold ? _boldFont : _regularFont);
        PInvoke.GetTextExtentPoint32W(_measure.Dc, text, text.Length, out var size);
        return size.cx;
    }

    public void Draw(PixelBuffer target, string text, int x, int y, uint color, bool bold)
    {
        if (text.Length == 0)
        {
            return;
        }

        var canvas = Prepare(target, bold ? _boldFont : _regularFont, color);
        PInvoke.TextOut(canvas.Dc, x, y, text, text.Length);
        CopyTextPixels(canvas, target, color);
    }

    public void DrawWithMnemonic(PixelBuffer target, string text, int x, int y, uint color)
    {
        if (text.Length == 0)
        {
            return;
        }

        var canvas = Prepare(target, _regularFont, color);
        var rect = new RECT(x, y, target.Width, target.Height);
        fixed (char* chars = text)
        {
            PInvoke.DrawText(canvas.Dc, chars, text.Length, ref rect, DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_NOCLIP);
        }

        CopyTextPixels(canvas, target, color);
    }

    public void Dispose()
    {
        _scratch?.Dispose();
        _measure.Dispose();
        PInvoke.DeleteObject(_regularFont);
        PInvoke.DeleteObject(_boldFont);
    }

    private static HFONT CreateFont(string face, int height, int weight)
    {
        fixed (char* name = face)
        {
            return PInvoke.CreateFont(
                height, 0, 0, 0, weight, 0, 0, 0, FONT_CHARSET.DEFAULT_CHARSET, 0, 0,
                FONT_QUALITY.NONANTIALIASED_QUALITY, 0, name);
        }
    }

    private static void CopyTextPixels(GdiCanvas canvas, PixelBuffer target, uint color)
    {
        var source = canvas.Pixels;
        var opaque = color | 0xFF000000;
        for (var i = 0; i < target.Pixels.Length; i++)
        {
            if ((source[i] >> 24) == 0)
            {
                target.Pixels[i] = opaque;
            }
        }
    }

    private GdiCanvas Prepare(PixelBuffer target, HFONT font, uint color)
    {
        if (_scratch is null || _scratch.Width != target.Width || _scratch.Height != target.Height)
        {
            _scratch?.Dispose();
            _scratch = new GdiCanvas(target.Width, target.Height);
        }

        _scratch.Pixels.Fill(0xFF000000);
        PInvoke.SelectObject(_scratch.Dc, font);
        PInvoke.SetTextColor(_scratch.Dc, NativeHelpers.Rgb(color & 0xFFFFFF));
        return _scratch;
    }
}
