using CoreAnimation;
using OfficeAssistant.Core.Platform;

namespace OfficeAssistant.Mac.Platform;

/// <summary>
/// A borderless, non-activating floating panel that shows a per-pixel-alpha bitmap in a
/// layer scaled with nearest-neighbour filtering. Clicks on fully transparent pixels go to
/// the windows underneath (AppKit does that for non-opaque windows). The balloon's panel can
/// become the key window, for typing, without activating the app.
/// </summary>
public sealed class MacSurface : IFloatingSurface
{
    private readonly FloatingPanel _panel;
    private readonly SurfaceView _view;
    private readonly NSObject _resignObserver;

    public MacSurface(SurfaceKind kind)
    {
        _panel = new FloatingPanel(kind == SurfaceKind.Balloon);
        _view = new SurfaceView(this, kind == SurfaceKind.Balloon);
        _panel.ContentView = _view;
        _resignObserver = NSWindow.Notifications.ObserveDidResignKey(_panel, (_, _) => Deactivated?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? PointerPressed;

    public event EventHandler? PointerMoved;

    public event EventHandler? PointerReleased;

    public event EventHandler? DoubleClicked;

    public event EventHandler? SecondaryClicked;

    public event EventHandler? CaptureLost;

    public event EventHandler? Deactivated;

    public event EventHandler<KeyInput>? KeyDown;

    public event EventHandler<char>? CharacterTyped;

    public int X { get; private set; }

    public int Y { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int Scale { get; private set; } = 1;

    public bool IsVisible { get; private set; }

    public (int X, int Y) CursorPosition
    {
        get
        {
            var location = NSEvent.CurrentMouseLocation;
            return ((int)Math.Floor(location.X), (int)Math.Floor(MacDisplays.PrimaryHeight - location.Y));
        }
    }

    public KeyModifiers Modifiers => ToModifiers(NSEvent.CurrentModifierFlags);

    public Func<CursorKind>? CursorProvider { get; set; }

    public void Present(ReadOnlySpan<uint> pixels, int width, int height, int x, int y, int scale)
    {
        Scale = Math.Max(1, scale);
        X = x;
        Y = y;
        Width = width * Scale;
        Height = height * Scale;

        var bytes = new byte[width * height * 4];
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixels[..(width * height)]).CopyTo(bytes);
        using var colorSpace = CGColorSpace.CreateSrgb();
        using var provider = new CGDataProvider(bytes);
        using var image = new CGImage(
            width, height, 8, 32, width * 4, colorSpace,
            CGBitmapFlags.ByteOrder32Little | CGBitmapFlags.PremultipliedFirst,
            provider, null, false, CGColorRenderingIntent.Default);

        CATransaction.Begin();
        CATransaction.DisableActions = true;
        _view.Layer!.Contents = image;
        _panel.SetFrame(MacDisplays.ToAppKit(x, y, Width, Height), display: false);
        CATransaction.Commit();

        // The shape changed, so the window server must recompute where clicks go through.
        _panel.InvalidateShadow();
    }

    public void Show(bool activate = false)
    {
        _panel.OrderFrontRegardless();
        if (activate)
        {
            Activate();
        }

        IsVisible = true;
    }

    public void Hide()
    {
        _panel.OrderOut(null);
        IsVisible = false;
    }

    public void MoveTo(int x, int y)
    {
        X = x;
        Y = y;
        _panel.SetFrameOrigin(MacDisplays.ToAppKit(x, y, Width, Height).Location);
    }

    public void Activate()
    {
        if (_panel.CanBecomeKeyWindow)
        {
            _panel.MakeKeyAndOrderFront(null);
            _panel.MakeFirstResponder(_view);
        }
    }

    // AppKit keeps sending a pressed button's drags and release to the view that got the press.
    public void CapturePointer()
    {
    }

    public void ReleasePointer() => CaptureLost?.Invoke(this, EventArgs.Empty);

    public int ShowMenu(int x, int y, IReadOnlyList<MenuItem> items)
    {
        var chosen = 0;
        using var menu = new NSMenu { AutoEnablesItems = false };
        foreach (var item in items)
        {
            if (item.Text is null)
            {
                menu.AddItem(NSMenuItem.SeparatorItem);
                continue;
            }

            var menuItem = new NSMenuItem(StripMnemonic(item.Text)) { Enabled = item.Enabled };
            var id = item.Id;
            menuItem.Activated += (_, _) => chosen = id;
            menu.AddItem(menuItem);
        }

        var location = _view.ConvertPointFromView(_panel.ConvertPointFromScreen(MacDisplays.ToAppKit(x, y)), null);
        menu.PopUpMenu(null, location, _view);
        return chosen;
    }

    public void Dispose()
    {
        _resignObserver.Dispose();
        _panel.OrderOut(null);
        _panel.Close();
        _panel.Dispose();
    }

    internal static KeyModifiers ToModifiers(NSEventModifierMask flags) =>
        (flags.HasFlag(NSEventModifierMask.ShiftKeyMask) ? KeyModifiers.Shift : KeyModifiers.None)
        | (flags.HasFlag(NSEventModifierMask.CommandKeyMask) ? KeyModifiers.Command : KeyModifiers.None)
        | (flags.HasFlag(NSEventModifierMask.AlternateKeyMask) ? KeyModifiers.Word : KeyModifiers.None);

    internal void RaisePressed() => PointerPressed?.Invoke(this, EventArgs.Empty);

    internal void RaiseMoved() => PointerMoved?.Invoke(this, EventArgs.Empty);

    internal void RaiseReleased() => PointerReleased?.Invoke(this, EventArgs.Empty);

    internal void RaiseDoubleClicked() => DoubleClicked?.Invoke(this, EventArgs.Empty);

    internal void RaiseSecondaryClicked() => SecondaryClicked?.Invoke(this, EventArgs.Empty);

    internal void RaiseKeyDown(KeyInput input) => KeyDown?.Invoke(this, input);

    internal void RaiseCharacterTyped(char c) => CharacterTyped?.Invoke(this, c);

    internal void UpdateCursor()
    {
        var cursor = CursorProvider?.Invoke() switch
        {
            CursorKind.Hand => NSCursor.PointingHandCursor,
            CursorKind.IBeam => NSCursor.IBeamCursor,
            _ => NSCursor.ArrowCursor,
        };
        cursor.Set();
    }

    private static string StripMnemonic(string text) => text.Replace("&&", "\0").Replace("&", string.Empty).Replace("\0", "&");

    /// <summary>The window: borderless, transparent, floating on every Space and over full-screen apps.</summary>
    private sealed class FloatingPanel : NSPanel
    {
        private readonly bool _canBecomeKey;

        public FloatingPanel(bool canBecomeKey)
            : base(new CGRect(0, 0, 1, 1), NSWindowStyle.Borderless | NSWindowStyle.NonactivatingPanel, NSBackingStore.Buffered, false)
        {
            _canBecomeKey = canBecomeKey;
            FloatingPanel = true;
            Level = NSWindowLevel.Floating;
            CollectionBehavior = NSWindowCollectionBehavior.CanJoinAllSpaces
                | NSWindowCollectionBehavior.FullScreenAuxiliary
                | NSWindowCollectionBehavior.IgnoresCycle;
            HidesOnDeactivate = false;
            IsOpaque = false;
            BackgroundColor = NSColor.Clear;
            HasShadow = false;
            ReleaseWhenClosed(false);
            AcceptsMouseMovedEvents = true;
        }

        public override bool CanBecomeKeyWindow => _canBecomeKey;

        public override bool CanBecomeMainWindow => false;
    }
}
