using Microsoft.UI.Xaml.Controls;
using OfficeAssistant.Presentation;

namespace OfficeAssistant;

/// <summary>Functions used in x:Bind to turn view model values into WinUI ones.</summary>
public static class XamlConverters
{
    public static InfoBarSeverity ToSeverity(McpServerStatus status) => status switch
    {
        McpServerStatus.Success => InfoBarSeverity.Success,
        McpServerStatus.Warning => InfoBarSeverity.Warning,
        McpServerStatus.Error => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    };
}
