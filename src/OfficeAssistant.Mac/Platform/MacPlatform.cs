using OfficeAssistant.Core.Platform;
using OfficeAssistant.Core.Rendering;

namespace OfficeAssistant.Mac.Platform;

/// <summary>The AppKit implementation: floating panels, the baked Tahoma, AVAudioPlayer and the Keychain.</summary>
public sealed class MacPlatform : IPlatform
{
    public IDisplays Displays { get; } = new MacDisplays();

    public IClipboard Clipboard { get; } = new MacClipboard();

    public ISecretStore Secrets { get; } = new KeychainSecretStore();

    // macOS has no user setting for this; 530 ms matches the system text fields.
    public int CaretBlinkMilliseconds => 530;

    public IFloatingSurface CreateSurface(SurfaceKind kind) => new MacSurface(kind);

    public IAudioOutput CreateAudioOutput() => new AvAudioOutput();

    public ITextRenderer CreateTextRenderer(string face, int pixelHeight) =>
        new BitmapFontRenderer(new CoreTextRenderer(face, pixelHeight));
}
