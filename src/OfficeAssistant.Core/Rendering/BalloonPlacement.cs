using OfficeAssistant.Core.Windows;

namespace OfficeAssistant.Core.Rendering;

/// <summary>Which side of the balloon the tail is on.</summary>
public enum BalloonTailSide
{
    /// <summary>Balloon above the character, tail pointing down.</summary>
    Bottom,

    /// <summary>Balloon below the character, tail pointing up.</summary>
    Top,
}

/// <summary>Where the character's frame is on screen (physical pixels) and its pixel size.</summary>
public readonly record struct BalloonFrame(int X, int Y, int Width, int Height, int Scale);

/// <summary>A screen rectangle in physical pixels.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom);

/// <summary>
/// Where a balloon goes next to the character. <see cref="Left"/> and <see cref="Top"/> are
/// the screen position of the balloon including its tail, in physical pixels;
/// <see cref="TailX"/> is where the tail opening starts, in balloon pixels from the box's left.
/// </summary>
public readonly record struct BalloonPlacement(int Left, int Top, BalloonTailSide TailSide, int TailX, int Scale, uint Dpi)
{
    /// <summary>Height of the tail below (or above) the box, in balloon pixels.</summary>
    public const int TailHeight = 15;

    /// <summary>Width of the tail opening in the border, in balloon pixels.</summary>
    public const int TailOpening = 8;

    // Measured from the screenshots: the tail opening starts 29 px into the character
    // frame and the box sits 6 px above the frame; the tail tip reaches 9 px into it.
    private const int TailOffsetInFrame = 29;
    private const int GapAboveFrame = 6;

    /// <summary>Places a box of <paramref name="boxWidth"/> x <paramref name="boxHeight"/> balloon pixels on the character's monitor.</summary>
    public static BalloonPlacement Place(BalloonFrame frame, int boxWidth, int boxHeight)
    {
        var centerX = frame.X + (frame.Width / 2);
        var centerY = frame.Y + (frame.Height / 2);
        var work = LayeredWindow.GetWorkArea(centerX, centerY);
        var dpi = LayeredWindow.GetDpiAt(centerX, centerY);
        return Place(frame, boxWidth, boxHeight, new ScreenRect(work.left, work.top, work.right, work.bottom), dpi);
    }

    /// <summary>
    /// Places the balloon above the character with the tail pointing at it, or below it
    /// when there is no room above, and keeps it inside <paramref name="work"/>.
    /// </summary>
    public static BalloonPlacement Place(BalloonFrame frame, int boxWidth, int boxHeight, ScreenRect work, uint dpi = 96)
    {
        var scale = Math.Max(1, frame.Scale);
        var screenWidth = boxWidth * scale;
        var screenHeight = boxHeight * scale;
        var totalHeight = (boxHeight + TailHeight) * scale;
        var tailScreenX = frame.X + (TailOffsetInFrame * scale);

        var tailSide = frame.Y - (GapAboveFrame * scale) - screenHeight >= work.Top
            ? BalloonTailSide.Bottom
            : BalloonTailSide.Top;

        var left = tailScreenX + ((TailOpening * scale) / 2) - (screenWidth / 2);
        left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - screenWidth));
        var tailX = Math.Clamp((tailScreenX - left) / scale, 9, Math.Max(9, boxWidth - 10 - TailOpening));

        var top = tailSide == BalloonTailSide.Bottom
            ? frame.Y - (GapAboveFrame * scale) - screenHeight
            : frame.Y + frame.Height + (GapAboveFrame * scale) - (TailHeight * scale);
        top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - totalHeight));

        return new BalloonPlacement(left, top, tailSide, tailX, scale, dpi);
    }
}
