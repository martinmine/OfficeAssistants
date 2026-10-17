namespace OfficeAssistant.Core.Platform;

public interface IClipboard
{
    /// <summary>The clipboard's text, or null when it holds none.</summary>
    string? GetText();

    void SetText(string text);
}
