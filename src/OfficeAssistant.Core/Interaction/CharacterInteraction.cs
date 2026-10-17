using OfficeAssistant.Core.Platform;

namespace OfficeAssistant.Core.Interaction;

/// <summary>
/// The floating assistant's mouse handling. Never takes focus; can be dragged anywhere,
/// clicked, double-clicked or right-clicked.
/// </summary>
public sealed class CharacterInteraction : IDisposable
{
    private const int DragThreshold = 3;

    private readonly IFloatingSurface _surface;
    private bool _pressed;
    private bool _dragging;
    private int _pressX;
    private int _pressY;
    private int _offsetX;
    private int _offsetY;

    public CharacterInteraction(IPlatform platform)
    {
        _surface = platform.CreateSurface(SurfaceKind.Character);
        _surface.PointerPressed += (_, _) => OnPressed();
        _surface.PointerMoved += (_, _) => OnMoved();
        _surface.PointerReleased += (_, _) => OnReleased();
        _surface.CaptureLost += (_, _) => _pressed = _dragging = false;
        _surface.DoubleClicked += (_, _) => DoubleClicked?.Invoke(this, EventArgs.Empty);
        _surface.SecondaryClicked += (_, _) => ContextMenuRequested?.Invoke(this, _surface.CursorPosition);
    }

    public event EventHandler? Clicked;

    public event EventHandler? DoubleClicked;

    /// <summary>Raised with the screen point to show the menu at.</summary>
    public event EventHandler<(int X, int Y)>? ContextMenuRequested;

    public event EventHandler? DragStarted;

    public event EventHandler? Moved;

    /// <summary>The window the character is drawn in.</summary>
    public IFloatingSurface Surface => _surface;

    /// <summary>Shows a popup menu at a screen point and returns the chosen id (0 if dismissed).</summary>
    public int ShowMenu(int x, int y, IReadOnlyList<MenuItem> items) => _surface.ShowMenu(x, y, items);

    public void Dispose() => _surface.Dispose();

    private void OnPressed()
    {
        var (x, y) = _surface.CursorPosition;
        _pressed = true;
        _dragging = false;
        _pressX = x;
        _pressY = y;
        _offsetX = x - _surface.X;
        _offsetY = y - _surface.Y;
        _surface.CapturePointer();
    }

    private void OnMoved()
    {
        if (!_pressed)
        {
            return;
        }

        var (x, y) = _surface.CursorPosition;
        if (!_dragging && (Math.Abs(x - _pressX) > DragThreshold || Math.Abs(y - _pressY) > DragThreshold))
        {
            _dragging = true;
            DragStarted?.Invoke(this, EventArgs.Empty);
        }

        if (_dragging)
        {
            _surface.MoveTo(x - _offsetX, y - _offsetY);
            Moved?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnReleased()
    {
        if (!_pressed)
        {
            return;
        }

        // Releasing the capture raises CaptureLost right away, which resets the drag state.
        var wasDragging = _dragging;
        _pressed = false;
        _dragging = false;
        _surface.ReleasePointer();
        if (!wasDragging)
        {
            Clicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
