using OfficeAssistant.Core.Platform;
using Windows.Win32;

namespace OfficeAssistant.Core.Windows;

/// <summary>The Win32 implementation: layered windows, GDI text, waveOut sound and DPAPI.</summary>
public sealed class WindowsPlatform : IPlatform
{
    private Win32Surface? _clipboardOwner;

    public WindowsPlatform()
    {
        Clipboard = new Win32Clipboard(() => _clipboardOwner?.Handle ?? 0);
    }

    public IDisplays Displays { get; } = new Win32Displays();

    public IClipboard Clipboard { get; }

    public ISecretStore Secrets { get; } = new DpapiSecretStore();

    public int CaretBlinkMilliseconds => (int)PInvoke.GetCaretBlinkTime();

    public IFloatingSurface CreateSurface(SurfaceKind kind)
    {
        var surface = new Win32Surface(kind);
        if (kind == SurfaceKind.Balloon)
        {
            // The balloon's text box is what copies and pastes.
            _clipboardOwner = surface;
        }

        return surface;
    }

    public IAudioOutput CreateAudioOutput() => new WaveOutAudio();

    public ITextRenderer CreateTextRenderer(string face, int pixelHeight) => new GdiTextRenderer(face, pixelHeight);
}
