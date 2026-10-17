using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace OfficeAssistant.Core.Windows;

/// <summary>
/// The floating assistant. Never takes focus; can be dragged anywhere, clicked or
/// right-clicked.
/// </summary>
public sealed unsafe class CharacterWindow : LayeredWindow
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
            case PInvoke.WM_MOUSEACTIVATE:
                return (nint)PInvoke.MA_NOACTIVATE;

            case PInvoke.WM_LBUTTONDOWN:
            {
                var (x, y) = CursorPosition();
                _pressed = true;
                _dragging = false;
                _pressX = x;
                _pressY = y;
                _offsetX = x - X;
                _offsetY = y - Y;
                PInvoke.SetCapture(Hwnd);
                return 0;
            }

            case PInvoke.WM_MOUSEMOVE when _pressed:
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

            case PInvoke.WM_LBUTTONUP when _pressed:
                _pressed = false;
                PInvoke.ReleaseCapture();
                if (!_dragging)
                {
                    Clicked?.Invoke(this, EventArgs.Empty);
                }

                _dragging = false;
                return 0;

            case PInvoke.WM_CAPTURECHANGED:
                _pressed = false;
                _dragging = false;
                return 0;

            case PInvoke.WM_LBUTTONDBLCLK:
                DoubleClicked?.Invoke(this, EventArgs.Empty);
                return 0;

            case PInvoke.WM_RBUTTONUP:
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
        using var menu = PInvoke.CreatePopupMenu_SafeHandle();
        foreach (var (id, text, enabled) in items)
        {
            if (text is null)
            {
                PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, null);
            }
            else
            {
                PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING | (enabled ? 0 : MENU_ITEM_FLAGS.MF_GRAYED), (nuint)id, text);
            }
        }

        // The menu needs a foreground owner to dismiss properly.
        PInvoke.SetForegroundWindow(Hwnd);

        // With TPM_RETURNCMD the "BOOL" result is the chosen command id.
        return PInvoke.TrackPopupMenuEx(
            menu, (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON), x, y, Hwnd, null);
    }
}
