using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace OfficeAssistant.Core.Rendering;

/// <summary>
/// A top-down 32-bit DIB selected into a memory DC, so it can be drawn on both with GDI
/// (for text) and directly through <see cref="Pixels"/>. Pixels are 0xAARRGGBB.
/// </summary>
internal sealed unsafe class GdiCanvas : IDisposable
{
    // Explicit handles: the DIB stays selected into the DC for the canvas's lifetime.
    private readonly HBITMAP _bitmap;
    private readonly HGDIOBJ _oldBitmap;
    private readonly uint* _bits;

    public GdiCanvas(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Dc = PInvoke.CreateCompatibleDC(default);

        _bitmap = CreateDib(Dc, Width, Height, out _bits);
        if (_bitmap.IsNull)
        {
            PInvoke.DeleteDC(Dc);
            throw new InvalidOperationException("CreateDIBSection failed.");
        }

        _oldBitmap = PInvoke.SelectObject(Dc, _bitmap);
        PInvoke.SetBkMode(Dc, BACKGROUND_MODE.TRANSPARENT);
    }

    public int Width { get; }

    public int Height { get; }

    public HDC Dc { get; }

    public Span<uint> Pixels
    {
        get
        {
            PInvoke.GdiFlush();
            return new Span<uint>(_bits, Width * Height);
        }
    }

    public void Dispose()
    {
        PInvoke.SelectObject(Dc, _oldBitmap);
        PInvoke.DeleteObject(_bitmap);
        PInvoke.DeleteDC(Dc);
    }

    /// <summary>Creates a top-down 32-bit DIB section and returns its pixel pointer.</summary>
    internal static HBITMAP CreateDib(HDC dc, int width, int height, out uint* bits)
    {
        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
            },
        };
        void* pointer;
        var bitmap = PInvoke.CreateDIBSection(dc, &info, DIB_USAGE.DIB_RGB_COLORS, &pointer, default, 0);
        bits = (uint*)pointer;
        return bitmap;
    }
}
