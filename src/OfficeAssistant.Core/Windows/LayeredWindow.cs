using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OfficeAssistant.Core.Rendering;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

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

    private static readonly Dictionary<nint, LayeredWindow> Windows = [];
    private static bool _classRegistered;

    private bool _visible;

    protected LayeredWindow(bool activatable)
    {
        RegisterClass();
        var exStyle = WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;
        if (!activatable)
        {
            exStyle |= WINDOW_EX_STYLE.WS_EX_NOACTIVATE;
        }

        HWND hwnd;
        fixed (char* className = ClassName)
        fixed (char* title = "Office Assistant")
        {
            hwnd = PInvoke.CreateWindowEx(
                exStyle, className, title, WINDOW_STYLE.WS_POPUP, 0, 0, 1, 1, default, default,
                (HINSTANCE)PInvoke.GetModuleHandle(default(PCWSTR)).Value, null);
        }

        Handle = hwnd;
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

    private protected HWND Hwnd => (HWND)Handle;

    public void Dispose()
    {
        if (Handle != 0)
        {
            Windows.Remove(Handle);
            PInvoke.DestroyWindow(Hwnd);
            Handle = 0;
        }

        GC.SuppressFinalize(this);
    }

    public void Show(bool activate = false)
    {
        PInvoke.ShowWindow(Hwnd, activate ? SHOW_WINDOW_CMD.SW_SHOW : SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        if (activate)
        {
            PInvoke.SetForegroundWindow(Hwnd);
        }

        _visible = true;
    }

    public void Hide()
    {
        PInvoke.ShowWindow(Hwnd, SHOW_WINDOW_CMD.SW_HIDE);
        _visible = false;
    }

    public void MoveTo(int x, int y)
    {
        X = x;
        Y = y;
        PInvoke.SetWindowPos(Hwnd, default, x, y, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    /// <summary>Shows a premultiplied 0xAARRGGBB bitmap at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public void Present(ReadOnlySpan<uint> pixels, int width, int height, int x, int y, int scale)
    {
        Scale = Math.Max(1, scale);
        X = x;
        Y = y;
        Width = width * Scale;
        Height = height * Scale;

        var screenDc = PInvoke.GetDC(default);
        var memoryDc = PInvoke.CreateCompatibleDC(screenDc);
        var bitmap = GdiCanvas.CreateDib(memoryDc, Width, Height, out var bits);
        var old = PInvoke.SelectObject(memoryDc, bitmap);
        try
        {
            var target = new Span<uint>(bits, Width * Height);
            ScaleNearest(pixels, width, height, target, Scale);

            var blend = new BLENDFUNCTION
            {
                BlendOp = (byte)PInvoke.AC_SRC_OVER,
                SourceConstantAlpha = 255,
                AlphaFormat = (byte)PInvoke.AC_SRC_ALPHA,
            };
            var position = new System.Drawing.Point(x, y);
            var size = new SIZE(Width, Height);
            var origin = default(System.Drawing.Point);
            PInvoke.UpdateLayeredWindow(
                Hwnd, screenDc, &position, &size, memoryDc, &origin, default, &blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);
        }
        finally
        {
            PInvoke.SelectObject(memoryDc, old);
            PInvoke.DeleteObject(bitmap);
            PInvoke.DeleteDC(memoryDc);
            _ = PInvoke.ReleaseDC(default, screenDc);
        }
    }

    /// <summary>Work area of the monitor nearest to a point, in physical pixels.</summary>
    internal static RECT GetWorkArea(int x, int y)
    {
        var monitor = PInvoke.MonitorFromPoint(new System.Drawing.Point(x, y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        PInvoke.GetMonitorInfo(monitor, ref info);
        return info.rcWork;
    }

    /// <summary>Effective DPI of the monitor nearest to a point (96 at 100% scaling).</summary>
    internal static uint GetDpiAt(int x, int y)
    {
        var monitor = PInvoke.MonitorFromPoint(new System.Drawing.Point(x, y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        return OperatingSystem.IsWindowsVersionAtLeast(10)
            && PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Succeeded
            ? dpiX
            : 96;
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
        PInvoke.GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    private protected static void SetCursor(PCWSTR cursorId) =>
        PInvoke.SetCursor(PInvoke.LoadCursor(default, cursorId));

    private static void RegisterClass()
    {
        if (_classRegistered)
        {
            return;
        }

        var className = Marshal.StringToHGlobalUni(ClassName); // Lives for the process.
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)sizeof(WNDCLASSEXW),
            style = WNDCLASS_STYLES.CS_DBLCLKS,

            // A static [UnmanagedCallersOnly] method: no delegate to keep alive.
            lpfnWndProc = &StaticWndProc,
            hInstance = (HINSTANCE)PInvoke.GetModuleHandle(default(PCWSTR)).Value,
            hCursor = PInvoke.LoadCursor(default, PInvoke.IDC_ARROW),
            lpszClassName = (char*)className,
        };
        if (PInvoke.RegisterClassEx(wc) == 0)
        {
            throw new InvalidOperationException($"RegisterClassEx failed ({Marshal.GetLastPInvokeError()}).");
        }

        _classRegistered = true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT StaticWndProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (Windows.TryGetValue(hwnd, out var window))
        {
            try
            {
                if (window.OnMessage(message, (nint)wParam.Value, lParam.Value) is { } result)
                {
                    return (LRESULT)result;
                }
            }
            catch (Exception ex)
            {
                // Never let exceptions unwind into native code.
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
