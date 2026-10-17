using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OfficeAssistant.Core.Rendering;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace OfficeAssistant.Core.Interop;

/// <summary>Gives a top-level window the outline of an Office XP balloon: a rounded box with a tail.</summary>
public static unsafe partial class BalloonWindowShape
{
    // The Office XP corners are 7 pixels deep.
    private const int CornerDiameter = 15;

    private const int GwlpWndProc = -4;

    private static readonly Dictionary<nint, nint> PreviousProcedures = [];

    /// <summary>
    /// Makes the whole of <paramref name="hwnd"/> its client area, so the outline can be drawn
    /// edge to edge. WinUI puts its frame styles back whenever it likes, so instead of
    /// removing them the window is subclassed to report no frame (WM_NCCALCSIZE).
    /// </summary>
    public static void RemoveFrame(nint hwnd)
    {
        if (PreviousProcedures.ContainsKey(hwnd))
        {
            return;
        }

        var window = (HWND)hwnd;
        delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT> procedure = &FramelessProcedure;
        PreviousProcedures[hwnd] = SetWindowLongPtr(hwnd, GwlpWndProc, (nint)procedure);
        PInvoke.SetWindowPos(
            window, default, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
            | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Clips <paramref name="hwnd"/> to the balloon outline (all sizes in balloon pixels,
    /// multiplied by <paramref name="scale"/>) and turns off the rounded corners and border
    /// that DWM would add.
    /// </summary>
    public static void Apply(nint hwnd, int boxWidth, int boxHeight, BalloonTailSide tailSide, int tailX, int scale)
    {
        var window = (HWND)hwnd;
        var s = Math.Max(1, scale);
        var width = boxWidth * s;
        var height = boxHeight * s;
        var tail = BalloonPlacement.TailHeight * s;
        var boxTop = tailSide == BalloonTailSide.Top ? tail : 0;
        var tailLeft = tailX * s;
        var tailRight = (tailX + BalloonPlacement.TailOpening) * s + 1;

        // The tail's left edge slants towards its tip; the right edge is vertical.
        System.Drawing.Point[] points = tailSide == BalloonTailSide.Bottom
            ? [new(tailLeft, height - 1), new(tailRight, height + tail), new(tailRight, height - 1)]
            : [new(tailLeft, boxTop + 1), new(tailRight, 0), new(tailRight, boxTop + 1)];

        var region = PInvoke.CreateRoundRectRgn(0, boxTop, width + 1, boxTop + height + 1, CornerDiameter * s, CornerDiameter * s);
        fixed (System.Drawing.Point* p = points)
        {
            var tailRegion = PInvoke.CreatePolygonRgn(p, points.Length, CREATE_POLYGON_RGN_MODE.WINDING);
            PInvoke.CombineRgn(region, region, tailRegion, RGN_COMBINE_MODE.RGN_OR);
            PInvoke.DeleteObject(tailRegion);
        }

        // On success the window owns the region.
        if (PInvoke.SetWindowRgn(window, region, true) == 0)
        {
            PInvoke.DeleteObject(region);
        }

        var corners = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;
        PInvoke.DwmSetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &corners, sizeof(DWM_WINDOW_CORNER_PREFERENCE));
        var noBorder = PInvoke.DWMWA_COLOR_NONE;
        PInvoke.DwmSetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR, &noBorder, sizeof(uint));
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
