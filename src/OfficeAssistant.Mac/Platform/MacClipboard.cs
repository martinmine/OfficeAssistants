using OfficeAssistant.Core.Platform;

namespace OfficeAssistant.Mac.Platform;

public sealed class MacClipboard : IClipboard
{
    public string? GetText() => NSPasteboard.GeneralPasteboard.GetStringForType(NSPasteboardType.String.GetConstant()!);

    public void SetText(string text)
    {
        var pasteboard = NSPasteboard.GeneralPasteboard;
        pasteboard.ClearContents();
        pasteboard.SetStringForType(text, NSPasteboardType.String.GetConstant()!);
    }
}
