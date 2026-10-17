using OfficeAssistant.Core.Rendering;
using Windows.Win32;

namespace OfficeAssistant.Core.Windows;

/// <summary>
/// The Office XP balloon, painted by <see cref="BalloonRenderer"/> and placed next to
/// the character. It never takes the focus: typing happens in an
/// <see cref="IBalloonTextInput"/> that sits over the Answer Wizard box while the user edits,
/// and the balloon draws the text itself again afterwards.
/// </summary>
public sealed class BalloonWindow : LayeredWindow
{
    // Measured from the screenshots: the tail opening starts 29 px into the character
    // frame and the box sits 6 px above the frame; the tail tip reaches 9 px into it.
    private const int TailOffsetInFrame = 29;
    private const int GapAboveFrame = 6;

    private readonly BalloonRenderer _renderer = new();
    private readonly BalloonState _state = new();
    private readonly IBalloonTextInput _input;
    private BalloonContent? _content;
    private string? _placeholder;
    private BalloonLayout? _layout;
    private BalloonBitmap? _bitmap;
    private (int X, int Y, int Width, int Height) _anchor;
    private int _anchorScale = 1;
    private BalloonHit _pressed = BalloonHit.None;

    public BalloonWindow(IBalloonTextInput input)
        : base(activatable: false)
    {
        _input = input;
        _input.Submitted += (_, _) => OnSubmitted();
        _input.Dismissed += (_, _) => Dismissed?.Invoke(this, EventArgs.Empty);
        _input.EditingEnded += (_, _) => OnEditingEnded();
    }

    /// <summary>A label (bulleted item or "See more...") was clicked.</summary>
    public event EventHandler<string>? LabelClicked;

    public event EventHandler<string>? ButtonClicked;

    /// <summary>Escape was pressed.</summary>
    public event EventHandler? Dismissed;

    public BalloonContent? Content => _content;

    /// <summary>Current text of the Answer Wizard box (empty when it still shows the placeholder).</summary>
    public string Question
    {
        get
        {
            var text = _input.IsEditing ? _input.Text : _state.TextBoxText;
            return text == _placeholder ? string.Empty : text;
        }
    }

    public bool HasTextBox => _content?.Blocks.Any(b => b is BalloonTextBox) == true;

    /// <summary>Shows content anchored to the character frame (screen pixels); with <paramref name="activate"/> the text box takes the focus.</summary>
    public void ShowContent(BalloonContent content, (int X, int Y, int Width, int Height) frame, int scale, bool activate)
    {
        _input.EndEditing();
        _content = content;
        var box = content.Blocks.OfType<BalloonTextBox>().FirstOrDefault();
        _state.TextBoxText = box?.InitialText ?? string.Empty;
        _placeholder = box is { IsPlaceholder: true } ? box.InitialText : null;
        _state.TextBoxFocused = false;
        _anchor = frame;
        _anchorScale = scale;
        _layout = _renderer.Measure(content, _state);
        Repaint();
        Show();
        if (activate && HasTextBox)
        {
            BeginEditing(selectAll: true);
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
        _input.EndEditing();
        _content = null;
        _layout = null;
        Hide();
    }

    protected override nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case PInvoke.WM_MOUSEACTIVATE:
                return (nint)PInvoke.MA_NOACTIVATE;

            case PInvoke.WM_SETCURSOR:
                UpdateCursor();
                return 1;

            case PInvoke.WM_LBUTTONDOWN:
            case PInvoke.WM_LBUTTONDBLCLK:
                // The second click of a double-click arrives instead of a button-down.
                _pressed = HitTestCursor();
                if (_pressed.Kind == BalloonHitKind.TextBox)
                {
                    // The question prompt is replaced as a whole, like in Office.
                    BeginEditing(selectAll: _state.TextBoxText == _placeholder);
                }

                return 0;

            case PInvoke.WM_LBUTTONUP:
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
        }

        return null;
    }

    private void BeginEditing(bool selectAll)
    {
        if (_bitmap?.TextBox is not { } box)
        {
            return;
        }

        _input.BeginEditing(TextBoxBounds(box), _state.TextBoxText, selectAll);
    }

    private void OnEditingEnded()
    {
        // The balloon draws the text itself again, without a highlighted selection.
        _state.TextBoxText = _input.Text;
        if (_layout is not null && IsVisible)
        {
            Repaint();
        }
    }

    private void OnSubmitted()
    {
        _state.TextBoxText = _input.Text;
        var buttons = _content?.Blocks.OfType<BalloonButtons>().FirstOrDefault();
        if (buttons is { DefaultIndex: >= 0 })
        {
            ButtonClicked?.Invoke(this, buttons.Buttons[buttons.DefaultIndex].Id);
        }
    }

    private BalloonTextBoxBounds TextBoxBounds(TextBoxBounds box) => new(
        X + (box.X * Scale),
        Y + (box.Y * Scale),
        box.Width * Scale,
        box.Height * Scale,
        Scale,
        GetDpiAt(X + (box.X * Scale), Y + (box.Y * Scale)),
        Handle);

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
        if (_input.IsEditing && _bitmap.TextBox is { } box)
        {
            _input.Move(TextBoxBounds(box));
        }
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
