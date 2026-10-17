using OfficeAssistant.Core.Platform;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace OfficeAssistant.Core.Windows;

/// <summary>Monitors in physical pixels (the process is per-monitor DPI aware).</summary>
public sealed unsafe class Win32Displays : IDisplays
{
    public PixelRect WorkAreaAt(int x, int y)
    {
        var monitor = PInvoke.MonitorFromPoint(new System.Drawing.Point(x, y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        PInvoke.GetMonitorInfo(monitor, ref info);
        var work = info.rcWork;
        return new PixelRect(work.left, work.top, work.right, work.bottom);
    }

    public uint DpiAt(int x, int y)
    {
        var monitor = PInvoke.MonitorFromPoint(new System.Drawing.Point(x, y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        return OperatingSystem.IsWindowsVersionAtLeast(10)
            && PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Succeeded
            ? dpiX
            : 96;
    }
}
