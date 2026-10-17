using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OfficeAssistant.Core.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace OfficeAssistant.Platform.Windows;

/// <summary>
/// A borderless top-most popup whose content is a per-pixel-alpha bitmap pushed with
/// UpdateLayeredWindow. Bitmaps are shown 1:1 in physical pixels (optionally enlarged
/// by an integer factor with nearest-neighbour sampling) so nothing is ever filtered.
/// A layered window ignores clicks on fully transparent pixels by itself. Must be used on
/// a thread with a message loop (the WinUI UI thread works).
/// </summary>
public sealed unsafe class Win32Surface : IFloatingSurface
{
    private const string ClassName = "OfficeAssistant.LayeredWindow";

    private static readonly Dictionary<nint, Win32Surface> Windows = [];
    private static bool _classRegistered;

    private readonly bool _activatable;
    private bool _visible;
    private bool _trackingLeave;

    // The DIB that Present draws into, kept while the window size stays the same.
    private HDC _memoryDc;
    private HBITMAP _bitmap;
    private HGDIOBJ _oldBitmap;
    private uint* _bits;
    private int _bitmapWidth;
    private int _bitmapHeight;

    public Win32Surface(SurfaceKind kind)
    {
        RegisterClass();
        _activatable = kind == SurfaceKind.Balloon;
        var exStyle = WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;
        if (!_activatable)
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

    public event EventHandler? PointerPressed;

    public event EventHandler? PointerMoved;

    public event EventHandler? PointerReleased;

    public event EventHandler? DoubleClicked;

    public event EventHandler? SecondaryClicked;

    public event EventHandler? CaptureLost;

    public event EventHandler? Deactivated;

    public event EventHandler<KeyInput>? KeyDown;

    public event EventHandler<char>? CharacterTyped;

    public nint Handle { get; private set; }

    public int X { get; private set; }

    public int Y { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int Scale { get; private set; } = 1;

    public bool IsVisible => _visible;

    public (int X, int Y) CursorPosition
    {
        get
        {
            PInvoke.GetCursorPos(out var point);
            return (point.X, point.Y);
        }
    }

    /// <summary>Ctrl stands for both <see cref="KeyModifiers.Command"/> and <see cref="KeyModifiers.Word"/>.</summary>
    public KeyModifiers Modifiers =>
        (NativeHelpers.IsKeyDown(VIRTUAL_KEY.VK_SHIFT) ? KeyModifiers.Shift : KeyModifiers.None)
        | (NativeHelpers.IsKeyDown(VIRTUAL_KEY.VK_CONTROL) ? KeyModifiers.Command | KeyModifiers.Word : KeyModifiers.None);

    public Func<CursorKind>? CursorProvider { get; set; }

    private HWND Hwnd => (HWND)Handle;

    public void Dispose()
    {
        if (Handle != 0)
        {
            Windows.Remove(Handle);
            PInvoke.DestroyWindow(Hwnd);
            Handle = 0;
        }

        FreeBitmap();
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

    public void Activate() => PInvoke.SetForegroundWindow(Hwnd);

    public void CapturePointer() => PInvoke.SetCapture(Hwnd);

    // Sends WM_CAPTURECHANGED right away, so CaptureLost is raised before this returns.
    public void ReleasePointer() => PInvoke.ReleaseCapture();

    /// <summary>Shows a premultiplied 0xAARRGGBB bitmap at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public void Present(ReadOnlySpan<uint> pixels, int width, int height, int x, int y, int scale)
    {
        Scale = Math.Max(1, scale);
        X = x;
        Y = y;
        Width = width * Scale;
        Height = height * Scale;

        EnsureBitmap(Width, Height);
        ScaleNearest(pixels, width, height, new Span<uint>(_bits, Width * Height), Scale);

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
            Hwnd, default, &position, &size, _memoryDc, &origin, default, &blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);
    }

    public int ShowMenu(int x, int y, IReadOnlyList<MenuItem> items)
    {
        using var menu = PInvoke.CreatePopupMenu_SafeHandle();
        foreach (var (id, text, enabled) in items)
        {
            if (text is null)
            {
                PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, null);
            }
            else
            {
                PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING | (enabled ? 0 : MENU_ITEM_FLAGS.MF_GRAYED), (nuint)id, text);
            }
        }

        // The menu needs a foreground owner to dismiss properly.
        PInvoke.SetForegroundWindow(Hwnd);

        // With TPM_RETURNCMD the "BOOL" result is the chosen command id.
        return PInvoke.TrackPopupMenuEx(
            menu, (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON), x, y, Hwnd, null);
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

    private static Key ToKey(VIRTUAL_KEY key) => key switch
    {
        VIRTUAL_KEY.VK_ESCAPE => Key.Escape,
        VIRTUAL_KEY.VK_LEFT => Key.Left,
        VIRTUAL_KEY.VK_RIGHT => Key.Right,
        VIRTUAL_KEY.VK_HOME => Key.Home,
        VIRTUAL_KEY.VK_END => Key.End,
        VIRTUAL_KEY.VK_DELETE => Key.Delete,
        VIRTUAL_KEY.VK_A => Key.A,
        VIRTUAL_KEY.VK_C => Key.C,
        VIRTUAL_KEY.VK_V => Key.V,
        VIRTUAL_KEY.VK_X => Key.X,
        _ => Key.Other,
    };

    private static PCWSTR ToCursorId(CursorKind cursor) => cursor switch
    {
        CursorKind.Hand => PInvoke.IDC_HAND,
        CursorKind.IBeam => PInvoke.IDC_IBEAM,
        _ => PInvoke.IDC_ARROW,
    };

    /// <summary>Handles a window message; returns null to fall through to DefWindowProc.</summary>
    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case PInvoke.WM_MOUSEACTIVATE when !_activatable:
                return (nint)PInvoke.MA_NOACTIVATE;

            case PInvoke.WM_ACTIVATE when _activatable:
                if (NativeHelpers.LowWord((nuint)wParam) == 0)
                {
                    Deactivated?.Invoke(this, EventArgs.Empty);
                }

                return 0;

            case PInvoke.WM_SETCURSOR when CursorProvider is { } provider:
                PInvoke.SetCursor(PInvoke.LoadCursor(default, ToCursorId(provider())));
                return 1;

            case PInvoke.WM_MOUSEMOVE:
                if (!_trackingLeave)
                {
                    var tme = new TRACKMOUSEEVENT
                    {
                        cbSize = (uint)sizeof(TRACKMOUSEEVENT),
                        dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE,
                        hwndTrack = Hwnd,
                    };
                    _trackingLeave = PInvoke.TrackMouseEvent(ref tme);
                }

                PointerMoved?.Invoke(this, EventArgs.Empty);
                return 0;

            case PInvoke.WM_MOUSELEAVE:
                _trackingLeave = false;
                return 0;

            case PInvoke.WM_LBUTTONDOWN:
                PointerPressed?.Invoke(this, EventArgs.Empty);
                return 0;

            case PInvoke.WM_LBUTTONDBLCLK:
                // The second click of a double-click arrives instead of a button-down.
                DoubleClicked?.Invoke(this, EventArgs.Empty);
                return 0;

            case PInvoke.WM_LBUTTONUP:
                PointerReleased?.Invoke(this, EventArgs.Empty);
                return 0;

            case PInvoke.WM_RBUTTONUP:
                SecondaryClicked?.Invoke(this, EventArgs.Empty);
                return 0;

            case PInvoke.WM_CAPTURECHANGED:
                CaptureLost?.Invoke(this, EventArgs.Empty);
                return 0;

            case PInvoke.WM_KEYDOWN:
                KeyDown?.Invoke(this, new KeyInput(ToKey((VIRTUAL_KEY)wParam), Modifiers));
                return 0;

            case PInvoke.WM_CHAR:
                CharacterTyped?.Invoke(this, (char)wParam);
                return 0;
        }

        return null;
    }

    /// <summary>Makes sure the memory DC holds a DIB of this size, creating it only when the size changed.</summary>
    private void EnsureBitmap(int width, int height)
    {
        if (!_bitmap.IsNull && width == _bitmapWidth && height == _bitmapHeight)
        {
            return;
        }

        FreeBitmap();
        _memoryDc = PInvoke.CreateCompatibleDC(default);
        _bitmap = GdiCanvas.CreateDib(_memoryDc, width, height, out _bits);
        _oldBitmap = PInvoke.SelectObject(_memoryDc, _bitmap);
        _bitmapWidth = width;
        _bitmapHeight = height;
    }

    private void FreeBitmap()
    {
        if (_memoryDc.IsNull)
        {
            return;
        }

        PInvoke.SelectObject(_memoryDc, _oldBitmap);
        PInvoke.DeleteObject(_bitmap);
        PInvoke.DeleteDC(_memoryDc);
        _memoryDc = default;
        _bitmap = default;
        _bits = null;
    }

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
