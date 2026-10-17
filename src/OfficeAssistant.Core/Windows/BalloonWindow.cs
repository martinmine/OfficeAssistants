using OfficeAssistant.Core.Interop;
using OfficeAssistant.Core.Rendering;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace OfficeAssistant.Core.Windows;

/// <summary>
/// The Office XP balloon, painted by <see cref="BalloonRenderer"/> and placed next to
/// the character. Handles its own clicks and the Answer Wizard text box.
/// </summary>
public sealed unsafe class BalloonWindow : LayeredWindow
{
    // Measured from the screenshots: the tail opening starts 29 px into the character
    // frame and the box sits 6 px above the frame; the tail tip reaches 9 px into it.
    private const int TailOffsetInFrame = 29;
    private const int GapAboveFrame = 6;
    private const int CaretTimerId = 1;

    private readonly BalloonRenderer _renderer = new();
    private readonly BalloonState _state = new();
    private BalloonContent? _content;
    private string? _placeholder;
    private BalloonLayout? _layout;
    private BalloonBitmap? _bitmap;
    private (int X, int Y, int Width, int Height) _anchor;
    private int _anchorScale = 1;
    private BalloonHit _pressed = BalloonHit.None;
    private bool _trackingLeave;
    private bool _selecting;

    public BalloonWindow()
        : base(activatable: true)
    {
    }

    /// <summary>A label (bulleted item or "See more...") was clicked.</summary>
    public event EventHandler<string>? LabelClicked;

    public event EventHandler<string>? ButtonClicked;

    /// <summary>Escape was pressed.</summary>
    public event EventHandler? Dismissed;

    public BalloonContent? Content => _content;

    /// <summary>Current text of the Answer Wizard box (empty when it still shows the placeholder).</summary>
    public string Question => _state.TextBoxText == _placeholder ? string.Empty : _state.TextBoxText;

    public bool HasTextBox => _content?.Blocks.Any(b => b is BalloonTextBox) == true;

    /// <summary>Shows content anchored to the character frame (screen pixels).</summary>
    public void ShowContent(BalloonContent content, (int X, int Y, int Width, int Height) frame, int scale, bool activate)
    {
        _content = content;
        var box = content.Blocks.OfType<BalloonTextBox>().FirstOrDefault();
        _state.TextBoxText = box?.InitialText ?? string.Empty;
        _placeholder = box is { IsPlaceholder: true } ? box.InitialText : null;
        _state.TextBoxFocused = false;
        _selecting = false;
        _anchor = frame;
        _anchorScale = scale;
        _layout = _renderer.Measure(content, _state);
        Repaint();
        Show(activate && HasTextBox);
        if (activate && HasTextBox)
        {
            PInvoke.SetForegroundWindow(Hwnd);
            _state.SelectAll();
            SetTextBoxFocus(true);
        }
    }

    /// <summary>Re-positions the balloon after the character moved.</summary>
    public void UpdateAnchor((int X, int Y, int Width, int Height) frame, int scale)
    {
        _anchor = frame;
        _anchorScale = scale;
        if (_layout is not null && IsVisible)
        {
            Repaint();
        }
    }

    public void Close()
    {
        PInvoke.KillTimer(Hwnd, CaretTimerId);
        if (_selecting)
        {
            _selecting = false;
            PInvoke.ReleaseCapture();
        }

        _content = null;
        _layout = null;
        Hide();
    }

    protected override nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case PInvoke.WM_ACTIVATE:
                if (NativeHelpers.LowWord((nuint)wParam) == 0)
                {
                    // Deactivated: the box loses focus and hides its selection.
                    SetTextBoxFocus(false);
                }

                return 0;

            case PInvoke.WM_SETCURSOR:
                UpdateCursor();
                return 1;

            case PInvoke.WM_MOUSEMOVE:
                if (!_trackingLeave)
                {
                    var tme = new TRACKMOUSEEVENT
                    {
                        cbSize = (uint)sizeof(TRACKMOUSEEVENT),
                        dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE,
                        hwndTrack = Hwnd,
                    };
                    _trackingLeave = PInvoke.TrackMouseEvent(ref tme);
                }

                if (_selecting)
                {
                    _state.Select(_state.AnchorIndex, TextIndexAtCursor());
                    Repaint();
                }

                return 0;

            case PInvoke.WM_MOUSELEAVE:
                _trackingLeave = false;
                return 0;

            case PInvoke.WM_LBUTTONDOWN:
                _pressed = HitTestCursor();
                if (_pressed.Kind == BalloonHitKind.TextBox)
                {
                    OnTextBoxMouseDown();
                }

                return 0;

            case PInvoke.WM_LBUTTONDBLCLK:
                // The second click of a double-click arrives instead of a button-down.
                _pressed = HitTestCursor();
                if (_pressed.Kind == BalloonHitKind.TextBox && _state.TextBoxFocused)
                {
                    var (start, end) = TextEditing.WordAt(_state.TextBoxText, TextIndexAtCursor());
                    _state.Select(start, end);
                    Repaint();
                }

                return 0;

            case PInvoke.WM_LBUTTONUP:
            {
                if (_selecting)
                {
                    _selecting = false;
                    PInvoke.ReleaseCapture();
                    return 0;
                }

                var hit = HitTestCursor();
                var pressed = _pressed;
                _pressed = BalloonHit.None;
                if (hit != pressed || hit.Id is null)
                {
                    return 0;
                }

                if (hit.Kind == BalloonHitKind.Label)
                {
                    LabelClicked?.Invoke(this, hit.Id);
                }
                else if (hit.Kind == BalloonHitKind.Button)
                {
                    ButtonClicked?.Invoke(this, hit.Id);
                }

                return 0;
            }

            case PInvoke.WM_CAPTURECHANGED:
                _selecting = false;
                return 0;

            case PInvoke.WM_TIMER when wParam == CaretTimerId:
                _state.CaretVisible = !_state.CaretVisible;
                Repaint();
                return 0;

            case PInvoke.WM_KEYDOWN:
                OnKeyDown((VIRTUAL_KEY)wParam);
                return 0;

            case PInvoke.WM_CHAR:
                OnChar((char)wParam);
                return 0;
        }

        return null;
    }

    private void OnTextBoxMouseDown()
    {
        if (!_state.TextBoxFocused)
        {
            PInvoke.SetForegroundWindow(Hwnd);

            // The question prompt is replaced as a whole, like in Office.
            if (_state.TextBoxText == _placeholder)
            {
                _state.SelectAll();
                SetTextBoxFocus(true);
                return;
            }

            SetTextBoxFocus(true);
        }

        var index = TextIndexAtCursor();
        _state.Select(NativeHelpers.IsKeyDown(VIRTUAL_KEY.VK_SHIFT) ? _state.AnchorIndex : index, index);
        _state.CaretVisible = true;
        _selecting = true;
        PInvoke.SetCapture(Hwnd);
        Repaint();
    }

    private void OnKeyDown(VIRTUAL_KEY key)
    {
        if (key == VIRTUAL_KEY.VK_ESCAPE)
        {
            Dismissed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!_state.TextBoxFocused)
        {
            return;
        }

        var shift = NativeHelpers.IsKeyDown(VIRTUAL_KEY.VK_SHIFT);
        var control = NativeHelpers.IsKeyDown(VIRTUAL_KEY.VK_CONTROL);
        var text = _state.TextBoxText;
        var caret = _state.CaretIndex;

        switch (key)
        {
            // Without Shift, arrows first collapse an existing selection to its edge.
            case VIRTUAL_KEY.VK_LEFT when !shift && !control && _state.HasSelection:
                MoveCaret(_state.SelectionStart, shift);
                break;
            case VIRTUAL_KEY.VK_RIGHT when !shift && !control && _state.HasSelection:
                MoveCaret(_state.SelectionEnd, shift);
                break;
            case VIRTUAL_KEY.VK_LEFT:
                MoveCaret(control ? TextEditing.PreviousWordStart(text, caret) : Math.Max(0, caret - 1), shift);
                break;
            case VIRTUAL_KEY.VK_RIGHT:
                MoveCaret(control ? TextEditing.NextWordStart(text, caret) : Math.Min(text.Length, caret + 1), shift);
                break;
            case VIRTUAL_KEY.VK_HOME:
                MoveCaret(0, shift);
                break;
            case VIRTUAL_KEY.VK_END:
                MoveCaret(text.Length, shift);
                break;
            case VIRTUAL_KEY.VK_DELETE:
                if (!_state.HasSelection)
                {
                    _state.Select(caret, Math.Min(text.Length, caret + 1));
                }

                _state.ReplaceSelection(string.Empty);
                break;
            case VIRTUAL_KEY.VK_A when control:
                _state.SelectAll();
                break;
            case VIRTUAL_KEY.VK_C when control && _state.HasSelection:
                Clipboard.SetText(Hwnd, _state.SelectedText);
                return;
            case VIRTUAL_KEY.VK_X when control && _state.HasSelection:
                Clipboard.SetText(Hwnd, _state.SelectedText);
                _state.ReplaceSelection(string.Empty);
                break;
            case VIRTUAL_KEY.VK_V when control:
                var paste = Clipboard.GetText(Hwnd);
                if (string.IsNullOrEmpty(paste))
                {
                    return;
                }

                _state.ReplaceSelection(paste.ReplaceLineEndings(" "));
                break;
            default:
                return;
        }

        _state.CaretVisible = true;
        Repaint();
    }

    private void MoveCaret(int index, bool extend) =>
        _state.Select(extend ? _state.AnchorIndex : index, index);

    private void OnChar(char c)
    {
        if (c == '\r')
        {
            var buttons = _content?.Blocks.OfType<BalloonButtons>().FirstOrDefault();
            if (buttons is { DefaultIndex: >= 0 })
            {
                ButtonClicked?.Invoke(this, buttons.Buttons[buttons.DefaultIndex].Id);
            }

            return;
        }

        if (!_state.TextBoxFocused)
        {
            return;
        }

        if (c == '\b')
        {
            if (!_state.HasSelection && _state.CaretIndex > 0)
            {
                _state.Select(_state.CaretIndex - 1, _state.CaretIndex);
            }

            _state.ReplaceSelection(string.Empty);
        }
        else if (!char.IsControl(c))
        {
            _state.ReplaceSelection(c.ToString());
        }
        else
        {
            return;
        }

        _state.CaretVisible = true;
        Repaint();
    }

    private void SetTextBoxFocus(bool focused)
    {
        if (_state.TextBoxFocused == focused || _layout is null || !HasTextBox)
        {
            return;
        }

        _state.TextBoxFocused = focused;
        _state.CaretVisible = focused;
        if (focused)
        {
            PInvoke.SetTimer(Hwnd, CaretTimerId, PInvoke.GetCaretBlinkTime(), null);
        }
        else
        {
            PInvoke.KillTimer(Hwnd, CaretTimerId);
        }

        Repaint();
    }

    private int TextIndexAtCursor()
    {
        if (_bitmap is null)
        {
            return 0;
        }

        var (x, y) = CursorPosition();
        return _renderer.TextIndexFromPoint(_bitmap, (x - X) / Scale, (y - Y) / Scale);
    }

    private void Repaint()
    {
        if (_layout is null)
        {
            return;
        }

        var scale = _anchorScale;
        var (frameX, frameY, frameWidth, frameHeight) = _anchor;
        var work = GetWorkArea(frameX + (frameWidth / 2), frameY + (frameHeight / 2));
        var boxWidth = _layout.Width * scale;
        var totalHeight = (_layout.Height + BalloonRenderer.TailHeight) * scale;
        var tailScreenX = frameX + (TailOffsetInFrame * scale);

        // Prefer above the character; flip below when there is no room.
        var tailSide = frameY - (GapAboveFrame * scale) - (_layout.Height * scale) >= work.top
            ? BalloonTailSide.Bottom
            : BalloonTailSide.Top;

        var left = tailScreenX + ((BalloonRenderer.TailOpening * scale) / 2) - (boxWidth / 2);
        left = Math.Clamp(left, work.left, Math.Max(work.left, work.right - boxWidth));
        var tailX = (tailScreenX - left) / scale;

        _bitmap = _renderer.Render(_layout, _state, tailSide, tailX);
        var top = tailSide == BalloonTailSide.Bottom
            ? frameY - (GapAboveFrame * scale) - (_layout.Height * scale)
            : frameY + frameHeight + (GapAboveFrame * scale) - (BalloonRenderer.TailHeight * scale);
        top = Math.Clamp(top, work.top, Math.Max(work.top, work.bottom - totalHeight));

        Present(_bitmap.Pixels, _bitmap.Width, _bitmap.Height, left, top, scale);
    }

    private BalloonHit HitTestCursor()
    {
        if (_bitmap is null)
        {
            return BalloonHit.None;
        }

        var (x, y) = CursorPosition();
        return _bitmap.HitTest((x - X) / Scale, (y - Y) / Scale);
    }

    private void UpdateCursor()
    {
        var hit = HitTestCursor();
        SetCursor(hit.Kind switch
        {
            BalloonHitKind.Label => PInvoke.IDC_HAND,
            BalloonHitKind.TextBox => PInvoke.IDC_IBEAM,
            _ => PInvoke.IDC_ARROW,
        });
    }
}
