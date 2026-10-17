using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using OfficeAssistant.Core.Interop;
using OfficeAssistant.Core.Rendering;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;

namespace OfficeAssistant.Balloon;

/// <summary>
/// The Office XP assistant balloon, drawn with XAML in a borderless top-most window that is
/// clipped to the balloon's outline. Its text box is a real <see cref="TextBox"/>, so IME
/// input, the emoji panel, spell checking and screen readers all work.
/// </summary>
public sealed partial class BalloonWindow : Window, IBalloonView
{
    // Metrics in balloon pixels, measured from the Office XP screenshots.
    private const int Padding = 5;
    private const int MinimumWidth = 184;
    private const int MaximumWidth = 300;
    private const int LabelIndent = 27;
    private const int LabelRightPadding = 8;
    private const int IconSize = 32;
    private const int ButtonHeight = 22;
    private const int ButtonMinimumWidth = 60;
    private const int ButtonTextPadding = 22;
    private const int ButtonGap = 12;

    private BalloonFrame _frame;
    private TextBox? _textBox;
    private string? _placeholder;
    private BalloonButtons? _buttons;
    private int _boxWidth = MinimumWidth;
    private int _boxHeight;
    private double _appliedScale;
    private bool _visible;
    private bool _closed;

    public BalloonWindow()
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
        BalloonWindowShape.RemoveFrame(Handle);

        Root.Loaded += (_, _) => Root.XamlRoot.Changed += (_, _) => OnXamlRootChanged();
        Closed += (_, _) => _closed = true;
    }

    public event EventHandler<string>? LabelClicked;

    public event EventHandler<string>? ButtonClicked;

    public event EventHandler? Dismissed;

    public bool IsVisible => _visible && !_closed;

    private nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    public string Question => _textBox is null || _textBox.Text == _placeholder ? string.Empty : _textBox.Text;

    public void Show(BalloonContent content, BalloonFrame frame, bool activate)
    {
        if (_closed)
        {
            return;
        }

        _frame = frame;
        Build(content);
        Place();

        var takeFocus = activate && _textBox is not null;
        AppWindow.Show(takeFocus);
        _visible = true;
        if (takeFocus)
        {
            Activate();
            _textBox!.Focus(FocusState.Programmatic);
            _textBox.SelectAll();
        }
    }

    public void UpdateFrame(BalloonFrame frame)
    {
        _frame = frame;
        if (IsVisible)
        {
            Place();
        }
    }

    public new void Close()
    {
        _visible = false;
        if (!_closed)
        {
            AppWindow.Hide();
        }
    }

    public void Dispose()
    {
        if (!_closed)
        {
            base.Close();
        }
    }

    private static int DefaultSpacing(BalloonBlock? previous, BalloonBlock block) => (previous, block) switch
    {
        (null, _) => 0,
        (_, BalloonLabel or BalloonSeeMore or BalloonSeparator) => 8,
        (BalloonIconHeading, _) => 8,
        (BalloonSeparator, BalloonButtons) => 8,
        (_, BalloonButtons) => 9,
        _ => 16,
    };

    /// <summary>"&amp;Ask" becomes "Ask" with the A underlined and Alt+A as its access key.</summary>
    private static (string Plain, int Mnemonic) ParseMnemonic(string text)
    {
        var index = text.IndexOf('&');
        return index < 0 || index == text.Length - 1 ? (text, -1) : (text.Remove(index, 1), index);
    }

    private static Image CreateImage(Sprite sprite, int scale)
    {
        // Enlarged by whole pixels here, so XAML shows the sprite 1:1 and keeps it sharp.
        var width = sprite.Width * scale;
        var height = sprite.Height * scale;
        var bitmap = new WriteableBitmap(width, height);
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                BitConverter.TryWriteBytes(pixels.AsSpan(((y * width) + x) * 4), sprite.Pixels[((y / scale) * sprite.Width) + (x / scale)]);
            }
        }

        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels);
        }

        return new Image { Source = bitmap, Width = sprite.Width, Height = sprite.Height, Stretch = Stretch.Fill };
    }

    private TextBlock CreateText(string text, bool bold = false, Brush? foreground = null)
    {
        var block = new TextBlock { Text = text, Style = (Style)Root.Resources["BalloonTextStyle"] };
        if (bold)
        {
            block.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        }

        if (foreground is not null)
        {
            block.Foreground = foreground;
        }

        return block;
    }

    private double TextWidth(string text, bool bold = false)
    {
        var width = 0.0;
        foreach (var line in text.Split('\n'))
        {
            var block = CreateText(line, bold);
            block.TextWrapping = TextWrapping.NoWrap;
            block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            width = Math.Max(width, block.DesiredSize.Width);
        }

        return Math.Ceiling(width);
    }

    /// <summary>The box width: as for Office, wide enough for headings, labels and buttons, from 184 to 300.</summary>
    private int ComputeWidth(BalloonContent content)
    {
        var width = (double)MinimumWidth;
        var hasBodyText = content.Blocks.Any(b => b is BalloonText { Bold: false });
        foreach (var block in content.Blocks)
        {
            width = Math.Max(width, block switch
            {
                BalloonText { Bold: true } heading when !hasBodyText => TextWidth(heading.Text, bold: true) + (2 * Padding),
                BalloonIconHeading heading => Padding + IconSize + Padding + TextWidth(heading.Text, bold: true) + Padding,
                BalloonLabel label => LabelIndent + TextWidth(label.Text) + LabelRightPadding,
                BalloonButtons buttons => ButtonGroupWidth(buttons) + (2 * 6),
                _ => 0,
            });
        }

        return (int)Math.Min(width, MaximumWidth);
    }

    private int ButtonWidth(BalloonButtons buttons) =>
        (int)buttons.Buttons.Max(b => Math.Max(ButtonMinimumWidth, TextWidth(ParseMnemonic(b.Text).Plain) + ButtonTextPadding));

    private int ButtonGroupWidth(BalloonButtons buttons) =>
        (buttons.Buttons.Count * ButtonWidth(buttons)) + ((buttons.Buttons.Count - 1) * ButtonGap);

    private void Build(BalloonContent content)
    {
        Blocks.Children.Clear();
        _textBox = null;
        _placeholder = null;
        _buttons = null;
        _boxWidth = ComputeWidth(content);
        var contentWidth = _boxWidth - (2 * Padding);
        var scale = Math.Max(1, _frame.Scale);

        BalloonBlock? previous = null;
        foreach (var block in content.Blocks)
        {
            FrameworkElement element = block switch
            {
                BalloonText text => CreateText(text.Text, text.Bold),
                BalloonIconHeading heading => CreateIconHeading(heading, scale),
                BalloonLabel label => CreateLink(label.Text, label.Id, CreateImage(BalloonSprites.Bullet, scale), new Thickness(9 - Padding, 4, 0, 0), (Brush)Root.Resources["BalloonLabelBrush"], contentWidth),
                BalloonSeeMore seeMore => CreateLink(seeMore.Text, seeMore.Id, CreateImage(BalloonSprites.SeeMoreArrow, scale), new Thickness(6 - Padding, 5, 0, 0), null, contentWidth),
                BalloonTextBox box => CreateTextBox(box),
                BalloonSeparator => new Rectangle { Height = 1, Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xB2, 0xB2, 0xB2)) },
                BalloonButtons buttons => CreateButtons(buttons),
                _ => new Grid(),
            };

            element.Width = contentWidth;
            element.Margin = new Thickness(0, block.SpaceBefore ?? DefaultSpacing(previous, block), 0, 0);
            Blocks.Children.Add(element);
            previous = block;
        }

        Blocks.Width = contentWidth;
        Blocks.Margin = new Thickness(0);
        Blocks.Measure(new Size(contentWidth, double.PositiveInfinity));
        _boxHeight = (int)Math.Ceiling(Blocks.DesiredSize.Height) + (2 * Padding) + 1;
    }

    private Grid CreateIconHeading(BalloonIconHeading heading, int scale)
    {
        var grid = new Grid { Height = IconSize };
        var icon = CreateImage(BalloonSprites.Warning, scale);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        var text = CreateText(heading.Text, bold: true);
        text.Margin = new Thickness(IconSize + Padding, 0, 0, 0);
        text.VerticalAlignment = VerticalAlignment.Bottom;
        grid.Children.Add(icon);
        grid.Children.Add(text);
        return grid;
    }

    private BalloonLink CreateLink(string text, string id, Image marker, Thickness markerMargin, Brush? foreground, int contentWidth)
    {
        marker.HorizontalAlignment = HorizontalAlignment.Left;
        marker.VerticalAlignment = VerticalAlignment.Top;
        marker.Margin = markerMargin;
        AutomationProperties.SetAccessibilityView(marker, AccessibilityView.Raw);

        var label = CreateText(text, foreground: foreground);
        label.Margin = new Thickness(LabelIndent - Padding, 0, 0, 0);
        label.Width = contentWidth - (LabelIndent - Padding) - (LabelRightPadding - Padding);
        label.HorizontalAlignment = HorizontalAlignment.Left;

        var content = new Grid();
        content.Children.Add(marker);
        content.Children.Add(label);
        var link = new BalloonLink { Content = content, Style = (Style)Root.Resources["BalloonLinkStyle"] };
        AutomationProperties.SetName(link, text);
        link.Click += (_, _) => LabelClicked?.Invoke(this, id);
        return link;
    }

    private TextBox CreateTextBox(BalloonTextBox box)
    {
        _textBox = new TextBox { Text = box.InitialText, Style = (Style)Root.Resources["BalloonTextBoxStyle"] };
        _placeholder = box.IsPlaceholder ? box.InitialText : null;
        AutomationProperties.SetName(_textBox, "Question");
        if (_placeholder is not null)
        {
            AutomationProperties.SetHelpText(_textBox, _placeholder);
        }

        // The question prompt is replaced as a whole, like in Office.
        _textBox.GotFocus += (sender, _) =>
        {
            if (sender is TextBox { } textBox && textBox.Text == _placeholder)
            {
                DispatcherQueue.TryEnqueue(() => textBox.SelectAll());
            }
        };
        return _textBox;
    }

    private Canvas CreateButtons(BalloonButtons buttons)
    {
        _buttons = buttons;
        var canvas = new Canvas { Height = ButtonHeight };
        var count = buttons.Buttons.Count;
        var buttonWidth = ButtonWidth(buttons);

        // One button is centred; more spread over at least the minimum content width.
        var span = count == 1 ? buttonWidth : Math.Max(MinimumWidth - 12, ButtonGroupWidth(buttons));
        var start = (_boxWidth - span) / 2;
        for (var i = 0; i < count; i++)
        {
            var left = count == 1
                ? start
                : start + (int)Math.Round(i * (span - buttonWidth) / (double)(count - 1), MidpointRounding.AwayFromZero);
            var button = CreateButton(buttons.Buttons[i], buttonWidth);
            Canvas.SetLeft(button, left - Padding);
            canvas.Children.Add(button);
        }

        return canvas;
    }

    private Button CreateButton(BalloonButton definition, int width)
    {
        var (plain, mnemonic) = ParseMnemonic(definition.Text);
        var text = new TextBlock { Style = (Style)Root.Resources["BalloonTextStyle"], TextWrapping = TextWrapping.NoWrap };
        if (mnemonic < 0)
        {
            text.Text = plain;
        }
        else
        {
            text.Inlines.Add(new Run { Text = plain[..mnemonic] });
            var underline = new Underline();
            underline.Inlines.Add(new Run { Text = plain[mnemonic].ToString() });
            text.Inlines.Add(underline);
            text.Inlines.Add(new Run { Text = plain[(mnemonic + 1)..] });
        }

        var button = new Button { Content = text, Width = width, Style = (Style)Root.Resources["BalloonButtonStyle"] };
        if (mnemonic >= 0)
        {
            button.AccessKey = plain[mnemonic].ToString();
        }

        AutomationProperties.SetName(button, plain);
        button.Click += (_, _) => ButtonClicked?.Invoke(this, definition.Id);
        return button;
    }

    private void Place()
    {
        var placement = BalloonPlacement.Place(_frame, _boxWidth, _boxHeight);
        var scale = placement.Scale;
        var tailOnTop = placement.TailSide == BalloonTailSide.Top;

        Blocks.Margin = new Thickness(Padding, Padding + (tailOnTop ? BalloonPlacement.TailHeight : 0), Padding, 0);
        Scaled.Width = _boxWidth;
        Scaled.Height = _boxHeight + BalloonPlacement.TailHeight;
        Shape.Data = CreateOutline(_boxWidth, _boxHeight, placement.TailSide, placement.TailX);

        AppWindow.MoveAndResize(new RectInt32(placement.Left, placement.Top, _boxWidth * scale, (_boxHeight + BalloonPlacement.TailHeight) * scale));
        BalloonWindowShape.Apply(Handle, _boxWidth, _boxHeight, placement.TailSide, placement.TailX, scale);
        ApplyScale(Root.XamlRoot?.RasterizationScale ?? (placement.Dpi / 96.0));
    }

    /// <summary>One balloon pixel is <see cref="BalloonFrame.Scale"/> screen pixels, whatever the display scaling.</summary>
    private void ApplyScale(double rasterizationScale)
    {
        var scale = Math.Max(1, _frame.Scale) / rasterizationScale;
        _appliedScale = rasterizationScale;
        Scaled.RenderTransform = new ScaleTransform { ScaleX = scale, ScaleY = scale };
    }

    private void OnXamlRootChanged()
    {
        // Moved to a monitor with another scaling: keep balloon pixels the same size.
        if (Root.XamlRoot is { } root && root.RasterizationScale != _appliedScale && IsVisible)
        {
            Place();
        }
    }

    /// <summary>The rounded box with its tail, in balloon pixels (on pixel centres, for a crisp 1 px line).</summary>
    private static PathGeometry CreateOutline(int width, int height, BalloonTailSide tailSide, int tailX)
    {
        const double Radius = 7;
        var tail = BalloonPlacement.TailHeight;
        var top = (tailSide == BalloonTailSide.Top ? tail : 0) + 0.5;
        var bottom = top + height - 1;
        var left = 0.5;
        var right = width - 0.5;
        var tailStart = tailX + 0.5;
        var tailEnd = tailX + BalloonPlacement.TailOpening + 0.5;

        var figure = new PathFigure { StartPoint = new Point(left + Radius, top), IsClosed = true, IsFilled = true };
        void Line(double x, double y) => figure.Segments.Add(new LineSegment { Point = new Point(x, y) });
        void Arc(double x, double y) => figure.Segments.Add(new ArcSegment
        {
            Point = new Point(x, y),
            Size = new Size(Radius, Radius),
            SweepDirection = SweepDirection.Clockwise,
        });

        if (tailSide == BalloonTailSide.Top)
        {
            Line(tailStart, top);
            Line(tailEnd, top - tail);
            Line(tailEnd, top);
        }

        Line(right - Radius, top);
        Arc(right, top + Radius);
        Line(right, bottom - Radius);
        Arc(right - Radius, bottom);
        if (tailSide == BalloonTailSide.Bottom)
        {
            Line(tailEnd, bottom);
            Line(tailEnd, bottom + tail);
            Line(tailStart, bottom);
        }

        Line(left + Radius, bottom);
        Arc(left, bottom - Radius);
        Line(left, top + Radius);
        Arc(left + Radius, top);

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Dismissed?.Invoke(this, EventArgs.Empty);
        }
        else if (e.Key == VirtualKey.Enter && _buttons is { DefaultIndex: >= 0 } buttons)
        {
            e.Handled = true;
            ButtonClicked?.Invoke(this, buttons.Buttons[buttons.DefaultIndex].Id);
        }
    }

    /// <summary>A bulleted label or "See more...": a button that looks like a link.</summary>
    private sealed partial class BalloonLink : Button
    {
        public BalloonLink() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }
}
