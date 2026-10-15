using OfficeAssistant.Core.Interop;

namespace OfficeAssistant.Core.Windows;

/// <summary>
/// The floating assistant. Never takes focus; can be dragged anywhere, clicked or
/// right-clicked.
/// </summary>
public sealed class CharacterWindow : LayeredWindow
{
    private const int DragThreshold = 3;

    private bool _pressed;
    private bool _dragging;
    private int _pressX;
    private int _pressY;
    private int _offsetX;
    private int _offsetY;

    public CharacterWindow()
        : base(activatable: false)
    {
    }

    public event EventHandler? Clicked;

    public event EventHandler? DoubleClicked;

    public event EventHandler<(int X, int Y)>? ContextMenuRequested;

    public event EventHandler? DragStarted;

    public event EventHandler? Moved;

    protected override nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case Native.WM_MOUSEACTIVATE:
                return Native.MA_NOACTIVATE;

            case Native.WM_LBUTTONDOWN:
            {
                var (x, y) = CursorPosition();
                _pressed = true;
                _dragging = false;
                _pressX = x;
                _pressY = y;
                _offsetX = x - X;
                _offsetY = y - Y;
                Native.SetCapture(Handle);
                return 0;
            }

            case Native.WM_MOUSEMOVE when _pressed:
            {
                var (x, y) = CursorPosition();
                if (!_dragging && (Math.Abs(x - _pressX) > DragThreshold || Math.Abs(y - _pressY) > DragThreshold))
                {
                    _dragging = true;
                    DragStarted?.Invoke(this, EventArgs.Empty);
                }

                if (_dragging)
                {
                    MoveTo(x - _offsetX, y - _offsetY);
                    Moved?.Invoke(this, EventArgs.Empty);
                }

                return 0;
            }

            case Native.WM_LBUTTONUP when _pressed:
                _pressed = false;
                Native.ReleaseCapture();
                if (!_dragging)
                {
                    Clicked?.Invoke(this, EventArgs.Empty);
                }

                _dragging = false;
                return 0;

            case Native.WM_CAPTURECHANGED:
                _pressed = false;
                _dragging = false;
                return 0;

            case Native.WM_LBUTTONDBLCLK:
                DoubleClicked?.Invoke(this, EventArgs.Empty);
                return 0;

            case Native.WM_RBUTTONUP:
            {
                var (x, y) = CursorPosition();
                ContextMenuRequested?.Invoke(this, (x, y));
                return 0;
            }
        }

        return null;
    }

    /// <summary>Shows a popup menu at a screen point and returns the chosen id (0 if dismissed).</summary>
    public int ShowMenu(int x, int y, IEnumerable<(int Id, string? Text, bool Enabled)> items)
    {
        var menu = Native.CreatePopupMenu();
        try
        {
            foreach (var (id, text, enabled) in items)
            {
                if (text is null)
                {
                    Native.AppendMenu(menu, Native.MF_SEPARATOR, 0, null);
                }
                else
                {
                    Native.AppendMenu(menu, Native.MF_STRING | (enabled ? 0 : Native.MF_GRAYED), (nuint)id, text);
                }
            }

            // The menu needs a foreground owner to dismiss properly.
            Native.SetForegroundWindow(Handle);
            return Native.TrackPopupMenuEx(menu, Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON, x, y, Handle, 0);
        }
        finally
        {
            Native.DestroyMenu(menu);
        }
    }
}
