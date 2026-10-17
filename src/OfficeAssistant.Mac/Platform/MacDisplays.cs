using OfficeAssistant.Core.Platform;

namespace OfficeAssistant.Mac.Platform;

/// <summary>
/// Screens in points with a top-left origin at the primary screen's top-left corner, the
/// coordinate system Core uses. AppKit's origin is the primary screen's bottom-left corner.
/// </summary>
public sealed class MacDisplays : IDisplays
{
    /// <summary>Height of the primary screen (the one with the menu bar), which flips Y.</summary>
    public static nfloat PrimaryHeight => NSScreen.Screens.Length > 0 ? NSScreen.Screens[0].Frame.Height : 0;

    public PixelRect WorkAreaAt(int x, int y)
    {
        var point = ToAppKit(x, y);
        var screen = NSScreen.Screens.FirstOrDefault(s => s.Frame.Contains(point)) ?? NearestScreen(point);
        var area = screen.VisibleFrame;
        var left = (int)Math.Ceiling(area.X);
        var top = (int)Math.Ceiling(PrimaryHeight - area.Bottom);
        return new PixelRect(left, top, (int)Math.Floor(area.Right), (int)Math.Floor(PrimaryHeight - area.Y));
    }

    /// <summary>One art pixel per point: Retina screens double it in hardware, crisply.</summary>
    public uint DpiAt(int x, int y) => 96;

    /// <summary>Converts a top-left-origin point to AppKit screen coordinates.</summary>
    public static CGPoint ToAppKit(double x, double y) => new(x, PrimaryHeight - y);

    /// <summary>The frame AppKit wants for a rectangle given by its top-left corner.</summary>
    public static CGRect ToAppKit(int x, int y, int width, int height) => new(x, PrimaryHeight - y - height, width, height);

    private static NSScreen NearestScreen(CGPoint point) =>
        NSScreen.Screens.MinBy(s => Distance(s.Frame, point)) ?? NSScreen.MainScreen;

    private static double Distance(CGRect frame, CGPoint point)
    {
        var dx = Math.Max(0, Math.Max(frame.Left - point.X, point.X - frame.Right));
        var dy = Math.Max(0, Math.Max(frame.Bottom - point.Y, point.Y - frame.Top));
        return (dx * dx) + (dy * dy);
    }
}
