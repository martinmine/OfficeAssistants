using OfficeAssistant.Core.Interop;

namespace OfficeAssistant.Core.Rendering;

/// <summary>
/// A top-down 32-bit DIB selected into a memory DC, so it can be drawn on both with GDI
/// (for text) and directly through <see cref="Pixels"/>. Pixels are 0xAARRGGBB.
/// </summary>
internal sealed unsafe class GdiCanvas : IDisposable
{
    private readonly nint _bitmap;
    private readonly nint _oldBitmap;
    private readonly uint* _bits;

    public GdiCanvas(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Dc = Native.CreateCompatibleDC(0);

        var header = new Native.BITMAPINFOHEADER
        {
            BiSize = sizeof(Native.BITMAPINFOHEADER),
            BiWidth = Width,
            BiHeight = -Height,
            BiPlanes = 1,
            BiBitCount = 32,
        };
        _bitmap = Native.CreateDIBSection(Dc, header, 0, out var bits, 0, 0);
        if (_bitmap == 0)
        {
            Native.DeleteDC(Dc);
            throw new InvalidOperationException("CreateDIBSection failed.");
        }

        _bits = (uint*)bits;
        _oldBitmap = Native.SelectObject(Dc, _bitmap);
        Native.SetBkMode(Dc, Native.TRANSPARENT);
    }

    public int Width { get; }

    public int Height { get; }

    public nint Dc { get; }

    public nint Bitmap => _bitmap;

    public Span<uint> Pixels
    {
        get
        {
            Native.GdiFlush();
            return new Span<uint>(_bits, Width * Height);
        }
    }

    public void Dispose()
    {
        Native.SelectObject(Dc, _oldBitmap);
        Native.DeleteObject(_bitmap);
        Native.DeleteDC(Dc);
    }
}
