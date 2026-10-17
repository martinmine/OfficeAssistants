using CoreAnimation;
using ObjCRuntime;
using OfficeAssistant.Core.Platform;

namespace OfficeAssistant.Mac.Platform;

/// <summary>
/// The content of a <see cref="MacSurface"/>: shows the bitmap in its layer and turns
/// AppKit's mouse and keyboard events into the surface's platform-neutral events. Text
/// input goes through <see cref="INSTextInputClient"/>, so dead keys, ⌥ combinations and
/// input methods produce the characters they compose.
/// </summary>
internal sealed class SurfaceView : NSView, INSTextInputClient
{
    // Virtual key codes (Carbon's kVK_* values).
    private const ushort KeyReturn = 36;
    private const ushort KeyBackspace = 51;
    private const ushort KeyEscape = 53;
    private const ushort KeypadEnter = 76;
    private const ushort KeyHome = 115;
    private const ushort KeyForwardDelete = 117;
    private const ushort KeyEnd = 119;
    private const ushort KeyLeft = 123;
    private const ushort KeyRight = 124;

    private readonly MacSurface _surface;
    private readonly bool _acceptsKeys;
    private bool _secondaryPress;

    public SurfaceView(MacSurface surface, bool acceptsKeys)
    {
        _surface = surface;
        _acceptsKeys = acceptsKeys;
        WantsLayer = true;
        Layer!.MagnificationFilter = CALayer.FilterNearest;
        Layer.MinificationFilter = CALayer.FilterNearest;
        Layer.ContentsGravity = CALayer.GravityResize;
        AddTrackingArea(new NSTrackingArea(
            CGRect.Empty,
            NSTrackingAreaOptions.MouseMoved | NSTrackingAreaOptions.CursorUpdate | NSTrackingAreaOptions.ActiveAlways | NSTrackingAreaOptions.InVisibleRect,
            this,
            null));
    }

    public override bool AcceptsFirstResponder() => _acceptsKeys;

    // The first click on an inactive panel counts, as it does on Windows.
    public override bool AcceptsFirstMouse(NSEvent? theEvent) => true;

    public override bool IsFlipped => true;

    [Export("selectedRange")]
    public NSRange SelectedRange => new(NSRange.NotFound, 0);

    [Export("markedRange")]
    public NSRange MarkedRange => new(NSRange.NotFound, 0);

    [Export("hasMarkedText")]
    public bool HasMarkedText => false;

    [Export("validAttributesForMarkedText")]
    public NSString[] ValidAttributesForMarkedText => [];

    public override void MouseDown(NSEvent theEvent)
    {
        if (theEvent.ModifierFlags.HasFlag(NSEventModifierMask.ControlKeyMask))
        {
            // Control-click is the Mac's right click.
            _secondaryPress = true;
            return;
        }

        if (theEvent.ClickCount == 2)
        {
            _surface.RaiseDoubleClicked();
        }
        else
        {
            _surface.RaisePressed();
        }
    }

    public override void MouseDragged(NSEvent theEvent) => _surface.RaiseMoved();

    public override void MouseMoved(NSEvent theEvent)
    {
        _surface.UpdateCursor();
        _surface.RaiseMoved();
    }

    public override void CursorUpdate(NSEvent theEvent) => _surface.UpdateCursor();

    public override void MouseUp(NSEvent theEvent)
    {
        if (_secondaryPress)
        {
            _secondaryPress = false;
            _surface.RaiseSecondaryClicked();
            return;
        }

        _surface.RaiseReleased();
    }

    public override void RightMouseUp(NSEvent theEvent) => _surface.RaiseSecondaryClicked();

    public override void KeyDown(NSEvent theEvent)
    {
        var modifiers = MacSurface.ToModifiers(theEvent.ModifierFlags);
        var command = modifiers.HasFlag(KeyModifiers.Command);
        var withoutCommand = modifiers & ~KeyModifiers.Command;
        switch (theEvent.KeyCode)
        {
            case KeyEscape:
                _surface.RaiseKeyDown(new KeyInput(Key.Escape, modifiers));
                return;
            case KeyLeft:
                // ⌘← and ⌘→ go to the start and end of the line.
                _surface.RaiseKeyDown(new KeyInput(command ? Key.Home : Key.Left, withoutCommand));
                return;
            case KeyRight:
                _surface.RaiseKeyDown(new KeyInput(command ? Key.End : Key.Right, withoutCommand));
                return;
            case KeyHome:
                _surface.RaiseKeyDown(new KeyInput(Key.Home, modifiers));
                return;
            case KeyEnd:
                _surface.RaiseKeyDown(new KeyInput(Key.End, modifiers));
                return;
            case KeyForwardDelete:
                _surface.RaiseKeyDown(new KeyInput(Key.Delete, modifiers));
                return;
            case KeyBackspace:
                _surface.RaiseCharacterTyped('\b');
                return;
            case KeyReturn or KeypadEnter:
                _surface.RaiseCharacterTyped('\r');
                return;
        }

        if (command)
        {
            var key = theEvent.CharactersIgnoringModifiers?.ToLowerInvariant() switch
            {
                "a" => Key.A,
                "c" => Key.C,
                "v" => Key.V,
                "x" => Key.X,
                _ => Key.Other,
            };
            if (key != Key.Other)
            {
                _surface.RaiseKeyDown(new KeyInput(key, modifiers));
            }
            else
            {
                base.KeyDown(theEvent);
            }

            return;
        }

        InterpretKeyEvents([theEvent]);
    }

    [Export("insertText:replacementRange:")]
    public void InsertText(NSObject text, NSRange replacementRange)
    {
        var value = (text is NSAttributedString attributed ? attributed.Value : text.ToString()) ?? string.Empty;
        foreach (var c in value)
        {
            _surface.RaiseCharacterTyped(c);
        }
    }

    // Composition (a dead key's accent, an input method's candidates) isn't shown; the
    // composed text arrives through InsertText.
    [Export("setMarkedText:selectedRange:replacementRange:")]
    public void SetMarkedText(NSObject text, NSRange selectedRange, NSRange replacementRange)
    {
    }

    [Export("unmarkText")]
    public void UnmarkText()
    {
    }

    [Export("attributedSubstringForProposedRange:actualRange:")]
    public NSAttributedString? GetAttributedSubstring(NSRange range, out NSRange actualRange)
    {
        actualRange = new NSRange(NSRange.NotFound, 0);
        return null;
    }

    [Export("firstRectForCharacterRange:actualRange:")]
    public CGRect GetFirstRect(NSRange range, out NSRange actualRange)
    {
        // Input method windows open next to the balloon.
        actualRange = range;
        return Window?.ConvertRectToScreen(ConvertRectToView(Bounds, null)) ?? CGRect.Empty;
    }

    [Export("characterIndexForPoint:")]
    public nuint GetCharacterIndex(CGPoint point) => unchecked((nuint)NSRange.NotFound);

    // Keys AppKit maps to editing commands (arrows and so on) are handled in KeyDown; no beep for the rest.
    [Export("doCommandBySelector:")]
    public void DoCommandBySelector(Selector selector)
    {
    }
}
