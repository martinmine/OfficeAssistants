using OfficeAssistant.Core.Rendering;
using OfficeAssistant.Platform.Windows;

namespace AcsFormat.Tests;

internal static partial class TestFiles
{
    /// <summary>The balloon renderer with GDI text, as the app draws it on Windows.</summary>
    public static BalloonRenderer CreateBalloonRenderer() =>
        new(new GdiTextRenderer(BalloonRenderer.FontFace, BalloonRenderer.FontPixelHeight));
}
