using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace OfficeAssistant.Core.Interop;

/// <summary>Turns a WinUI window into a plain rectangle: no frame, rounded corners or DWM border.</summary>
public static unsafe partial class FramelessWindow
{
    private const int GwlpWndProc = -4;
    private const int GwlpHwndParent = -8;

    private static readonly Dictionary<nint, nint> PreviousProcedures = [];

    /// <summary>
    /// Makes the whole of <paramref name="hwnd"/> its client area. WinUI puts its frame styles
    /// back whenever it likes, so instead of removing them the window is subclassed to report
    /// no frame (WM_NCCALCSIZE).
    /// </summary>
    public static void Apply(nint hwnd)
    {
        if (PreviousProcedures.ContainsKey(hwnd))
        {
            return;
        }

        var window = (HWND)hwnd;
        delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT> procedure = &FramelessProcedure;
        PreviousProcedures[hwnd] = SetWindowLongPtr(hwnd, GwlpWndProc, (nint)procedure);

        var corners = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;
        PInvoke.DwmSetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &corners, sizeof(DWM_WINDOW_CORNER_PREFERENCE));
        var noBorder = PInvoke.DWMWA_COLOR_NONE;
        PInvoke.DwmSetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR, &noBorder, sizeof(uint));

        PInvoke.SetWindowPos(
            window, default, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
            | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    /// <summary>Makes <paramref name="owner"/> own <paramref name="hwnd"/>, so it always stays above it.</summary>
    public static void SetOwner(nint hwnd, nint owner) => SetWindowLongPtr(hwnd, GwlpHwndParent, owner);

    /// <summary>
    /// Puts <paramref name="hwnd"/> on top of the other top-most windows (a new owner only takes
    /// effect at the next z-order change) and gives it the keyboard focus. The process may do
    /// that because the user just clicked one of its windows.
    /// </summary>
    public static void BringToFront(nint hwnd)
    {
        var window = (HWND)hwnd;
        PInvoke.SetWindowPos(
            window, (HWND)(-1), 0, 0, 0, 0, // HWND_TOPMOST
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
        PInvoke.SetForegroundWindow(window);
    }

    // CsWin32 only generates SetWindowLongPtr for a specific CPU; this library is AnyCPU.
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT FramelessProcedure(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        // With wParam set, returning 0 keeps the proposed window rectangle as the client area.
        if (message == PInvoke.WM_NCCALCSIZE && wParam.Value != 0)
        {
            return default;
        }

        if (!PreviousProcedures.TryGetValue(hwnd, out var previous))
        {
            return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
        }

        if (message == PInvoke.WM_NCDESTROY)
        {
            PreviousProcedures.Remove(hwnd);
        }

        var procedure = (delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT>)previous;
        return PInvoke.CallWindowProc(procedure, hwnd, message, wParam, lParam);
    }
}
