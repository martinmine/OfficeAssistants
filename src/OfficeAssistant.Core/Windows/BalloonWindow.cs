using OfficeAssistant.Core.Interop;
using OfficeAssistant.Core.Rendering;

namespace OfficeAssistant.Core.Windows;

/// <summary>
/// The Office XP balloon, painted by <see cref="BalloonRenderer"/> and placed next to
/// the character. Handles its own clicks and the Answer Wizard text box.
/// </summary>
public sealed class BalloonWindow : LayeredWindow
{
    // Measured from the screenshots: the tail opening starts 29 px into the character
    // frame and the box sits 6 px above the frame; the tail tip reaches 9 px into it.
    private const int TailOffsetInFrame = 29;
    private const int GapAboveFrame = 6;
    private const int CaretTimerId = 1;

    private readonly BalloonRenderer _renderer = new();
    private readonly BalloonState _state = new();
    private BalloonContent? _content;
    private BalloonLayout? _layout;
    private BalloonBitmap? _bitmap;
    private (int X, int Y, int Width, int Height) _anchor;
    private int _anchorScale = 1;
    private BalloonHit _pressed = BalloonHit.None;
    private bool _trackingLeave;

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
    public string Question => _state.TextBoxText == BalloonScenarios.QuestionPlaceholder ? string.Empty : _state.TextBoxText;

    public bool HasTextBox => _content?.Blocks.Any(b => b is BalloonTextBox) == true;

    /// <summary>Shows content anchored to the character frame (screen pixels).</summary>
    public void ShowContent(BalloonContent content, (int X, int Y, int Width, int Height) frame, int scale, bool activate)
    {
        _content = content;
        _state.TextBoxText = content.Blocks.OfType<BalloonTextBox>().FirstOrDefault()?.InitialText ?? string.Empty;
        _state.TextBoxFocused = false;
        _state.TextBoxSelected = false;
        _anchor = frame;
        _anchorScale = scale;
        _layout = _renderer.Measure(content, _state);
        Repaint();
        Show(activate && HasTextBox);
        if (activate && HasTextBox)
        {
            FocusTextBox();
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
        Native.KillTimer(Handle, CaretTimerId);
        _content = null;
        _layout = null;
        Hide();
    }

    protected override nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case Native.WM_ACTIVATE:
                if (Native.LowWord(wParam) == 0)
                {
                    // Deactivated: the box loses focus and shows unselected text.
                    SetTextBoxFocus(false);
                }

                return 0;

            case Native.WM_SETCURSOR:
                UpdateCursor();
                return 1;

            case Native.WM_MOUSEMOVE:
                if (!_trackingLeave)
                {
                    var tme = new Native.TRACKMOUSEEVENT { CbSize = 16 + nint.Size, DwFlags = Native.TME_LEAVE, HwndTrack = Handle };
                    _trackingLeave = Native.TrackMouseEvent(ref tme);
                }

                return 0;

            case Native.WM_MOUSELEAVE:
                _trackingLeave = false;
                return 0;

            case Native.WM_LBUTTONDOWN:
                _pressed = HitTestCursor();
                if (_pressed.Kind == BalloonHitKind.TextBox)
                {
                    FocusTextBox();
                }

                return 0;

            case Native.WM_LBUTTONUP:
            {
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

            case Native.WM_TIMER when wParam == CaretTimerId:
                _state.CaretVisible = !_state.CaretVisible;
                Repaint();
                return 0;

            case Native.WM_KEYDOWN when (int)wParam == Native.VK_ESCAPE:
                Dismissed?.Invoke(this, EventArgs.Empty);
                return 0;

            case Native.WM_CHAR:
                OnChar((char)wParam);
                return 0;
        }

        return null;
    }

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

        if (!_state.TextBoxFocused || c == (char)Native.VK_ESCAPE)
        {
            return;
        }

        if (_state.TextBoxSelected)
        {
            _state.TextBoxText = string.Empty;
            _state.TextBoxSelected = false;
        }

        if (c == '\b')
        {
            if (_state.TextBoxText.Length > 0)
            {
                _state.TextBoxText = _state.TextBoxText[..^1];
            }
        }
        else if (!char.IsControl(c))
        {
            _state.TextBoxText += c;
        }

        _state.CaretVisible = true;
        Repaint();
    }

    private void FocusTextBox()
    {
        if (!HasTextBox)
        {
            return;
        }

        Native.SetForegroundWindow(Handle);
        SetTextBoxFocus(true);
    }

    private void SetTextBoxFocus(bool focused)
    {
        if (_state.TextBoxFocused == focused || _layout is null)
        {
            return;
        }

        _state.TextBoxFocused = focused;
        _state.TextBoxSelected = focused && _state.TextBoxText.Length > 0;
        _state.CaretVisible = focused;
        if (focused)
        {
            Native.SetTimer(Handle, CaretTimerId, Native.GetCaretBlinkTime(), 0);
        }
        else
        {
            Native.KillTimer(Handle, CaretTimerId);
        }

        Repaint();
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
        var tailSide = frameY - (GapAboveFrame * scale) - (_layout.Height * scale) >= work.Top
            ? BalloonTailSide.Bottom
            : BalloonTailSide.Top;

        var left = tailScreenX + ((BalloonRenderer.TailOpening * scale) / 2) - (boxWidth / 2);
        left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - boxWidth));
        var tailX = (tailScreenX - left) / scale;

        _bitmap = _renderer.Render(_layout, _state, tailSide, tailX);
        var top = tailSide == BalloonTailSide.Bottom
            ? frameY - (GapAboveFrame * scale) - (_layout.Height * scale)
            : frameY + frameHeight + (GapAboveFrame * scale) - (BalloonRenderer.TailHeight * scale);
        top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - totalHeight));

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
            BalloonHitKind.Label => Native.IDC_HAND,
            BalloonHitKind.TextBox => Native.IDC_IBEAM,
            _ => Native.IDC_ARROW,
        });
    }
}
