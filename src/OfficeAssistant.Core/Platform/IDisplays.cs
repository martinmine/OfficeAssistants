namespace OfficeAssistant.Core.Platform;

/// <summary>A rectangle in device pixels; <see cref="Right"/> and <see cref="Bottom"/> are exclusive.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

public interface IDisplays
{
    /// <summary>Work area (without task bar, menu bar or Dock) of the monitor nearest to a point.</summary>
    PixelRect WorkAreaAt(int x, int y);

    /// <summary>Effective DPI of the monitor nearest to a point, 96 meaning one art pixel per device pixel.</summary>
    uint DpiAt(int x, int y);
}
