using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace OfficeAssistant.Platform.Windows;

/// <summary>Small Win32 helpers that the generated <see cref="PInvoke"/> class does not provide.</summary>
internal static class NativeHelpers
{
    public static bool IsKeyDown(VIRTUAL_KEY key) => PInvoke.GetKeyState((int)key) < 0;

    public static int LowWord(nuint value) => unchecked((short)(ulong)value);

    /// <summary>COLORREF from 0xRRGGBB.</summary>
    public static COLORREF Rgb(uint rgb) => (COLORREF)(((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF));
}
