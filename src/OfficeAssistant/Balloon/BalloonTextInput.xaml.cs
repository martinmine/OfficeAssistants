using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OfficeAssistant.Core.Interop;
using OfficeAssistant.Core.Windows;
using Windows.Graphics;
using Windows.System;

namespace OfficeAssistant.Balloon;

/// <summary>
/// A real XAML <see cref="Microsoft.UI.Xaml.Controls.TextBox"/> in a frameless top-most window,
/// shown over the balloon's GDI-drawn Answer Wizard box while the user types. It brings IME
/// composition, the emoji panel, spell checking, the clipboard and UI Automation; when it
/// loses the focus it hides and the balloon draws the text with GDI again.
/// </summary>
public sealed partial class BalloonTextInput : Window, IBalloonTextInput
{
    private nint _owner;
    private bool _editing;
    private bool _closed;

    public BalloonTextInput()
    {
        InitializeComponent();

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        FramelessWindow.Apply(Handle);

        AutomationProperties.SetName(Box, "Question");
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && _editing)
            {
                StopEditing();
                EditingEnded?.Invoke(this, EventArgs.Empty);
            }
        };
        Closed += (_, _) => _closed = true;
    }

    public event EventHandler? Submitted;

    public event EventHandler? Dismissed;

    public event EventHandler? EditingEnded;

    public bool IsEditing => _editing;

    public string Text => Box.Text;

    private nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    public void BeginEditing(BalloonTextBoxBounds bounds, string text, bool selectAll)
    {
        if (_closed)
        {
            return;
        }

        if (_owner != bounds.Owner)
        {
            // Owned by the balloon, so it always stays above it.
            _owner = bounds.Owner;
            FramelessWindow.SetOwner(Handle, _owner);
        }

        Box.Text = text;
        Box.Select(text.Length, 0);
        Move(bounds);
        _editing = true;
        AppWindow.Show(activateWindow: true);
        FramelessWindow.BringToFront(Handle);
        Activate();
        Box.Focus(FocusState.Programmatic);
        if (selectAll)
        {
            // After the focus change, which would otherwise place the caret.
            DispatcherQueue.TryEnqueue(() => Box.SelectAll());
        }
    }

    public void Move(BalloonTextBoxBounds bounds)
    {
        if (_closed)
        {
            return;
        }

        // One balloon pixel is bounds.Scale screen pixels, whatever the display scaling.
        var scale = Math.Max(1, bounds.Scale);
        Box.Width = bounds.Width / scale;
        Box.Height = bounds.Height / scale;
        var zoom = scale / (bounds.Dpi / 96.0);
        Box.RenderTransform = new ScaleTransform { ScaleX = zoom, ScaleY = zoom };
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
    }

    public void EndEditing() => StopEditing();

    public void Dispose()
    {
        if (!_closed)
        {
            Close();
        }
    }

    private void StopEditing()
    {
        _editing = false;
        if (!_closed)
        {
            AppWindow.Hide();
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            Submitted?.Invoke(this, EventArgs.Empty);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Dismissed?.Invoke(this, EventArgs.Empty);
        }
    }
}
