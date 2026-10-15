using System.Runtime.InteropServices;
using OfficeAssistant.Core.Interop;

namespace OfficeAssistant.Core.Windows;

/// <summary>
/// A borderless top-most popup whose content is a per-pixel-alpha bitmap pushed with
/// UpdateLayeredWindow. Bitmaps are shown 1:1 in physical pixels (optionally enlarged
/// by an integer factor with nearest-neighbour sampling) so nothing is ever filtered.
/// Must be used on a thread with a message loop (the WinUI UI thread works).
/// </summary>
public abstract unsafe class LayeredWindow : IDisposable
{
    private const string ClassName = "OfficeAssistant.LayeredWindow";

    private static readonly Native.WndProc WndProcDelegate = StaticWndProc;
    private static readonly Dictionary<nint, LayeredWindow> Windows = [];
    private static bool _classRegistered;

    private bool _visible;

    protected LayeredWindow(bool activatable)
    {
        RegisterClass();
        var exStyle = Native.WS_EX_LAYERED | Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW;
        if (!activatable)
        {
            exStyle |= Native.WS_EX_NOACTIVATE;
        }

        Handle = Native.CreateWindowEx(exStyle, ClassName, "Office Assistant", Native.WS_POPUP, 0, 0, 1, 1, 0, 0, Native.GetModuleHandle(0), 0);
        if (Handle == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastPInvokeError()}).");
        }

        Windows[Handle] = this;
    }

    public nint Handle { get; private set; }

    /// <summary>Screen position of the window's top-left corner, in physical pixels.</summary>
    public int X { get; private set; }

    public int Y { get; private set; }

    /// <summary>Size on screen (bitmap size times <see cref="Scale"/>).</summary>
    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Integer enlargement factor applied to presented bitmaps.</summary>
    public int Scale { get; private set; } = 1;

    public bool IsVisible => _visible;

    public void Dispose()
    {
        if (Handle != 0)
        {
            Windows.Remove(Handle);
            Native.DestroyWindow(Handle);
            Handle = 0;
        }

        GC.SuppressFinalize(this);
    }

    public void Show(bool activate = false)
    {
        Native.ShowWindow(Handle, activate ? Native.SW_SHOW : Native.SW_SHOWNOACTIVATE);
        if (activate)
        {
            Native.SetForegroundWindow(Handle);
        }

        _visible = true;
    }

    public void Hide()
    {
        Native.ShowWindow(Handle, Native.SW_HIDE);
        _visible = false;
    }

    public void MoveTo(int x, int y)
    {
        X = x;
        Y = y;
        Native.SetWindowPos(Handle, 0, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    /// <summary>Shows a premultiplied 0xAARRGGBB bitmap at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public void Present(ReadOnlySpan<uint> pixels, int width, int height, int x, int y, int scale)
    {
        Scale = Math.Max(1, scale);
        X = x;
        Y = y;
        Width = width * Scale;
        Height = height * Scale;

        var screenDc = Native.GetDC(0);
        var memoryDc = Native.CreateCompatibleDC(screenDc);
        var header = new Native.BITMAPINFOHEADER
        {
            BiSize = sizeof(Native.BITMAPINFOHEADER),
            BiWidth = Width,
            BiHeight = -Height,
            BiPlanes = 1,
            BiBitCount = 32,
        };
        var bitmap = Native.CreateDIBSection(memoryDc, header, 0, out var bits, 0, 0);
        var old = Native.SelectObject(memoryDc, bitmap);
        try
        {
            var target = new Span<uint>((void*)bits, Width * Height);
            ScaleNearest(pixels, width, height, target, Scale);

            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                SourceConstantAlpha = 255,
                AlphaFormat = Native.AC_SRC_ALPHA,
            };
            Native.UpdateLayeredWindow(
                Handle, screenDc, new Native.POINT(x, y), new Native.SIZE(Width, Height),
                memoryDc, new Native.POINT(0, 0), 0, blend, Native.ULW_ALPHA);
        }
        finally
        {
            Native.SelectObject(memoryDc, old);
            Native.DeleteObject(bitmap);
            Native.DeleteDC(memoryDc);
            Native.ReleaseDC(0, screenDc);
        }
    }

    /// <summary>Work area of the monitor nearest to a point, in physical pixels.</summary>
    internal static Native.RECT GetWorkArea(int x, int y)
    {
        var monitor = Native.MonitorFromPoint(new Native.POINT(x, y), Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { CbSize = sizeof(Native.MONITORINFO) };
        Native.GetMonitorInfo(monitor, ref info);
        return info.RcWork;
    }

    internal static void ScaleNearest(ReadOnlySpan<uint> source, int width, int height, Span<uint> target, int scale)
    {
        if (scale == 1)
        {
            source[..(width * height)].CopyTo(target);
            return;
        }

        var targetWidth = width * scale;
        for (var y = 0; y < height; y++)
        {
            var row = target.Slice(y * scale * targetWidth, targetWidth);
            for (var x = 0; x < width; x++)
            {
                row.Slice(x * scale, scale).Fill(source[(y * width) + x]);
            }

            for (var r = 1; r < scale; r++)
            {
                row.CopyTo(target.Slice(((y * scale) + r) * targetWidth, targetWidth));
            }
        }
    }

    /// <summary>Handles a window message; return null to fall through to DefWindowProc.</summary>
    protected virtual nint? OnMessage(uint message, nint wParam, nint lParam) => null;

    protected static (int X, int Y) CursorPosition()
    {
        Native.GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    protected static void SetCursor(int cursorId) =>
        Native.SetCursor(Native.LoadCursor(0, Native.MakeIntResource(cursorId)));

    private static void RegisterClass()
    {
        if (_classRegistered)
        {
            return;
        }

        var className = Marshal.StringToHGlobalUni(ClassName); // Lives for the process.
        var wc = new Native.WNDCLASSEXW
        {
            CbSize = sizeof(Native.WNDCLASSEXW),
            Style = Native.CS_DBLCLKS,
            LpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcDelegate),
            HInstance = Native.GetModuleHandle(0),
            HCursor = Native.LoadCursor(0, Native.MakeIntResource(Native.IDC_ARROW)),
            LpszClassName = className,
        };
        if (Native.RegisterClassEx(wc) == 0)
        {
            throw new InvalidOperationException($"RegisterClassEx failed ({Marshal.GetLastPInvokeError()}).");
        }

        _classRegistered = true;
    }

    private static nint StaticWndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (Windows.TryGetValue(hwnd, out var window))
        {
            try
            {
                if (window.OnMessage(message, wParam, lParam) is { } result)
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                // Never let exceptions unwind into native code.
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        return Native.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
