namespace OfficeAssistant.Core.Platform;

/// <summary>
/// Everything the assistant needs from the operating system. The app shell creates one
/// instance on the UI thread and passes it to <see cref="AssistantController"/>.
/// </summary>
public interface IPlatform
{
    /// <summary>Monitors, their work areas and scaling.</summary>
    IDisplays Displays { get; }

    IClipboard Clipboard { get; }

    /// <summary>Protects the API key at rest.</summary>
    ISecretStore Secrets { get; }

    /// <summary>How long the text box caret stays on (and off) when blinking.</summary>
    int CaretBlinkMilliseconds { get; }

    /// <summary>Creates a borderless, top-most window that shows a per-pixel-alpha bitmap.</summary>
    IFloatingSurface CreateSurface(SurfaceKind kind);

    /// <summary>Creates an audio output. Each output plays independently, so outputs mix.</summary>
    IAudioOutput CreateAudioOutput();

    /// <summary>Creates the renderer for balloon text: <paramref name="face"/> at <paramref name="pixelHeight"/> pixels, without anti-aliasing.</summary>
    ITextRenderer CreateTextRenderer(string face, int pixelHeight);
}
