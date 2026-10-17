using System.Collections.ObjectModel;

namespace OfficeAssistant.Mac.Settings;

/// <summary>The character's animations in a table; clicking one plays it.</summary>
internal sealed class AnimationList : NSTableViewDataSource
{
    private readonly ObservableCollection<string> _animations;
    private readonly NSTableView _table;

    public AnimationList(ObservableCollection<string> animations, Action<string> play)
    {
        _animations = animations;
        _table = new NSTableView { HeaderView = null, UsesAlternatingRowBackgroundColors = true };
        _table.AddColumn(new NSTableColumn("name") { Title = "Animation" });
        _table.DataSource = this;
        _table.Delegate = new Cells(this);
        _table.Activated += (_, _) =>
        {
            var row = (int)_table.ClickedRow;
            if (row >= 0 && row < _animations.Count)
            {
                play(_animations[row]);
            }
        };
        animations.CollectionChanged += (_, _) => _table.ReloadData();

        View = new NSScrollView { DocumentView = _table, HasVerticalScroller = true, BorderType = NSBorderType.BezelBorder };
        View.HeightAnchor.ConstraintEqualTo(360).Active = true;
    }

    public NSScrollView View { get; }

    public override nint GetRowCount(NSTableView tableView) => _animations.Count;

    private sealed class Cells(AnimationList list) : NSTableViewDelegate
    {
        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn? tableColumn, nint row)
        {
            var cell = tableView.MakeView("cell", this) as NSTextField ?? NSTextField.CreateLabel(string.Empty);
            cell.Identifier = "cell";
            cell.StringValue = list._animations[(int)row];
            return cell;
        }
    }
}
