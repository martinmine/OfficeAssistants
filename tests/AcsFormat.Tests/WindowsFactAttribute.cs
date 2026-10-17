namespace AcsFormat.Tests;

/// <summary>A fact that needs Windows (GDI or DPAPI) and is skipped on other platforms.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Needs Windows.";
        }
    }
}
