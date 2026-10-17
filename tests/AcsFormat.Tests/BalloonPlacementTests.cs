using OfficeAssistant.Core.Rendering;

namespace AcsFormat.Tests;

public class BalloonPlacementTests
{
    private static readonly ScreenRect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void BalloonSitsAboveTheCharacterWithTheTailPointingAtIt()
    {
        var frame = new BalloonFrame(1500, 900, 124, 93, Scale: 1);

        var placement = BalloonPlacement.Place(frame, boxWidth: 184, boxHeight: 120, Work);

        Assert.Equal(BalloonTailSide.Bottom, placement.TailSide);
        Assert.Equal(900 - 6 - 120, placement.Top);

        // The tail opening starts 29 px into the character's frame.
        Assert.Equal(1500 + 29, placement.Left + placement.TailX);
    }

    [Fact]
    public void BalloonFlipsBelowWhenThereIsNoRoomAbove()
    {
        var frame = new BalloonFrame(500, 50, 124, 93, Scale: 1);

        var placement = BalloonPlacement.Place(frame, 184, 120, Work);

        Assert.Equal(BalloonTailSide.Top, placement.TailSide);
        Assert.Equal(50 + 93 + 6 - BalloonPlacement.TailHeight, placement.Top);
    }

    [Fact]
    public void BalloonStaysOnScreenAndTheTailStaysOnTheBox()
    {
        var frame = new BalloonFrame(1880, 900, 124, 93, Scale: 1);

        var placement = BalloonPlacement.Place(frame, 184, 120, Work);

        Assert.Equal(1920 - 184, placement.Left);
        Assert.InRange(placement.TailX, 9, 184 - 10 - BalloonPlacement.TailOpening);
    }

    [Fact]
    public void PixelSizeScalesTheBalloon()
    {
        var frame = new BalloonFrame(1000, 800, 248, 186, Scale: 2);

        var placement = BalloonPlacement.Place(frame, 184, 120, Work, dpi: 192);

        Assert.Equal(2, placement.Scale);
        Assert.Equal(192u, placement.Dpi);
        Assert.Equal(800 - 12 - 240, placement.Top);
        Assert.Equal(1000 + 58, placement.Left + (placement.TailX * 2));
    }
}
