namespace OfficeAssistant.Core.Platform;

/// <summary>What a floating surface is for, which decides how it treats focus.</summary>
public enum SurfaceKind
{
    /// <summary>The character: never takes focus, not even when clicked.</summary>
    Character,

    /// <summary>The balloon: takes keyboard focus when activated, for its text box.</summary>
    Balloon,
}

public enum CursorKind
{
    Arrow,
    Hand,
    IBeam,
}

/// <summary>A context menu entry. A null <see cref="Text"/> is a separator; '&amp;' marks a mnemonic.</summary>
public readonly record struct MenuItem(int Id, string? Text, bool Enabled = true);

/// <summary>
/// A borderless, top-most popup that shows a premultiplied 0xAARRGGBB bitmap, enlarged by an
/// integer factor with nearest-neighbour sampling. Fully transparent pixels let clicks through.
/// Coordinates are screen "device pixels" with a top-left origin (physical pixels on Windows,
/// points on macOS). Use it on the UI thread.
/// </summary>
/// <remarks>
/// Pointer events carry no position: read <see cref="CursorPosition"/> and
/// <see cref="Modifiers"/> in the handler. The second press of a double-click is reported as
/// <see cref="DoubleClicked"/> instead of <see cref="PointerPressed"/>; a
/// <see cref="PointerReleased"/> follows it as usual.
/// </remarks>
public interface IFloatingSurface : IDisposable
{
    /// <summary>Screen position of the top-left corner.</summary>
    int X { get; }

    int Y { get; }

    /// <summary>Size on screen (bitmap size times <see cref="Scale"/>).</summary>
    int Width { get; }

    int Height { get; }

    /// <summary>Integer enlargement factor of the last presented bitmap.</summary>
    int Scale { get; }

    bool IsVisible { get; }

    /// <summary>The pointer's screen position right now.</summary>
    (int X, int Y) CursorPosition { get; }

    /// <summary>Modifier keys held right now.</summary>
    KeyModifiers Modifiers { get; }

    /// <summary>Asked for the cursor to show while the pointer is over the surface; null shows the arrow.</summary>
    Func<CursorKind>? CursorProvider { get; set; }

    event EventHandler? PointerPressed;

    event EventHandler? PointerMoved;

    event EventHandler? PointerReleased;

    event EventHandler? DoubleClicked;

    /// <summary>The secondary (right) button was released over the surface.</summary>
    event EventHandler? SecondaryClicked;

    /// <summary>Pointer capture ended, by <see cref="ReleasePointer"/> or because the system took it.</summary>
    event EventHandler? CaptureLost;

    /// <summary>The surface lost keyboard focus.</summary>
    event EventHandler? Deactivated;

    event EventHandler<KeyInput>? KeyDown;

    /// <summary>A typed character: text, '\r' for Enter and '\b' for Backspace.</summary>
    event EventHandler<char>? CharacterTyped;

    /// <summary>Shows a bitmap at (<paramref name="x"/>, <paramref name="y"/>), enlarged <paramref name="scale"/> times.</summary>
    void Present(ReadOnlySpan<uint> pixels, int width, int height, int x, int y, int scale);

    void Show(bool activate = false);

    void Hide();

    void MoveTo(int x, int y);

    /// <summary>Gives the surface keyboard focus (only meaningful for <see cref="SurfaceKind.Balloon"/>).</summary>
    void Activate();

    /// <summary>Keeps sending pointer events to this surface while the button is held, wherever the pointer goes.</summary>
    void CapturePointer();

    void ReleasePointer();

    /// <summary>Shows a popup menu at a screen point and returns the chosen id (0 if dismissed).</summary>
    int ShowMenu(int x, int y, IReadOnlyList<MenuItem> items);
}
