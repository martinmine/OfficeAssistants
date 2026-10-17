namespace OfficeAssistant.Mac.Settings;

/// <summary>Builders for the settings window's AppKit controls, laid out in stack views.</summary>
internal static class Controls
{
    public static NSStackView Column(params NSView[] views)
    {
        var stack = NSStackView.FromViews(views);
        stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        stack.Alignment = NSLayoutAttribute.Leading;
        stack.Spacing = 10;
        return stack;
    }

    public static NSStackView Row(params NSView[] views)
    {
        var stack = NSStackView.FromViews(views);
        stack.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        stack.Alignment = NSLayoutAttribute.CenterY;
        stack.Spacing = 8;
        return stack;
    }

    /// <summary>A tab's content: a padded column whose rows stretch to the tab's width.</summary>
    public static NSView Page(params NSView[] views)
    {
        var column = Column(views);
        column.EdgeInsets = new NSEdgeInsets(20, 20, 20, 20);
        column.TranslatesAutoresizingMaskIntoConstraints = false;
        var page = new NSView();
        page.AddSubview(column);
        NSLayoutConstraint.ActivateConstraints(
        [
            column.TopAnchor.ConstraintEqualTo(page.TopAnchor),
            column.LeadingAnchor.ConstraintEqualTo(page.LeadingAnchor),
            column.TrailingAnchor.ConstraintEqualTo(page.TrailingAnchor),
            column.BottomAnchor.ConstraintLessThanOrEqualTo(page.BottomAnchor),
        ]);
        foreach (var view in views)
        {
            if (view is NSStackView or NSScrollView || view is NSTextField { Editable: false })
            {
                view.TrailingAnchor.ConstraintEqualTo(column.TrailingAnchor, -20).Active = true;
            }
        }

        return page;
    }

    public static NSTextField Heading(string text)
    {
        var label = NSTextField.CreateLabel(text);
        label.Font = NSFont.BoldSystemFontOfSize(NSFont.SystemFontSize + 2)!;
        return label;
    }

    public static NSTextField Label(string text = "")
    {
        var label = NSTextField.CreateWrappingLabel(text);
        label.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        return label;
    }

    public static NSTextField Caption(string text = "")
    {
        var label = Label(text);
        label.TextColor = NSColor.SecondaryLabel;
        label.Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize)!;
        return label;
    }

    public static NSButton Button(string title, Action action)
    {
        var button = NSButton.CreateButton(title, action);
        return button;
    }

    public static NSButton Checkbox(string title, Action<bool> changed)
    {
        NSButton? checkbox = null;
        checkbox = NSButton.CreateCheckbox(title, () => changed(checkbox!.State == NSCellStateValue.On));
        return checkbox;
    }

    public static NSPopUpButton Popup(IEnumerable<string> items, Action<int> selected)
    {
        var popup = new NSPopUpButton(CGRect.Empty, pullsDown: false);
        popup.AddItems([.. items]);
        popup.Activated += (_, _) => selected((int)popup.IndexOfSelectedItem);
        return popup;
    }

    /// <summary>A scrolling, multi-line text editor.</summary>
    public static (NSScrollView Scroll, NSTextView Text) TextEditor(nfloat height, bool monospaced)
    {
        var scroll = NSTextView.CreateScrollableTextView();
        scroll.BorderType = NSBorderType.BezelBorder;
        scroll.HeightAnchor.ConstraintEqualTo(height).Active = true;
        var text = (NSTextView)scroll.DocumentView!;
        text.RichText = false;
        text.AutomaticQuoteSubstitutionEnabled = false;
        text.AutomaticDashSubstitutionEnabled = false;
        text.AutomaticTextReplacementEnabled = false;
        text.Font = (monospaced ? NSFont.MonospacedSystemFont(NSFont.SystemFontSize, NSFontWeight.Regular) : NSFont.SystemFontOfSize(NSFont.SystemFontSize))!;
        return (scroll, text);
    }

    public static void Width(NSView view, nfloat width) => view.WidthAnchor.ConstraintEqualTo(width).Active = true;

    public static void MinWidth(NSView view, nfloat width) => view.WidthAnchor.ConstraintGreaterThanOrEqualTo(width).Active = true;
}
