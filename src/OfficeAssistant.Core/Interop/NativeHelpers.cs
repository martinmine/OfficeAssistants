using Windows.Win32.Foundation;

namespace OfficeAssistant.Core.Interop;

/// <summary>Small Win32 helpers that the generated <see cref="Windows.Win32.PInvoke"/> class does not provide.</summary>
internal static class NativeHelpers
{
    /// <summary>COLORREF from 0xRRGGBB.</summary>
    public static COLORREF Rgb(uint rgb) => (COLORREF)(((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF));
}
