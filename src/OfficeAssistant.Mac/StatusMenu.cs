using OfficeAssistant.Core;
using OfficeAssistant.Presentation;

namespace OfficeAssistant.Mac;

/// <summary>
/// The paper clip in the menu bar: show or hide the assistant, ask it something, pick
/// another assistant, sounds, settings and Quit. Rebuilt each time it opens.
/// </summary>
internal sealed class StatusMenu : NSMenuDelegate
{
    private readonly MainPageViewModel _viewModel;
    private readonly AssistantController _assistant;
    private readonly Action _openSettings;
    private readonly NSStatusItem _item;
    private readonly NSMenu _menu;

    public StatusMenu(MainPageViewModel viewModel, AssistantController assistant, Action openSettings)
    {
        _viewModel = viewModel;
        _assistant = assistant;
        _openSettings = openSettings;
        _menu = new NSMenu { AutoEnablesItems = false, Delegate = this };
        _item = NSStatusBar.SystemStatusBar.CreateStatusItem(NSStatusItemLength.Variable);
        _item.Button.Image = NSImage.GetSystemSymbol("paperclip", "Office Assistant");
        _item.Button.ToolTip = "Office Assistant";
        _item.Menu = _menu;
    }

    public override void MenuWillOpen(NSMenu menu)
    {
        menu.RemoveAllItems();
        var shown = _assistant.IsShown;
        Add(menu, shown ? "Hide Assistant" : "Show Assistant", () => _viewModel.IsAssistantShown = !shown);
        Add(menu, "Ask a Question...", () => _viewModel.AskAssistantCommand.Execute(null), enabled: _assistant.Character is not null);
        Add(menu, "Animate!", () => _viewModel.AnimateCommand.Execute(null), enabled: shown);
        Add(menu, "New Topic", () => _viewModel.NewConversationCommand.Execute(null));
        menu.AddItem(NSMenuItem.SeparatorItem);

        var choose = new NSMenu { AutoEnablesItems = false };
        for (var i = 0; i < _viewModel.Assistants.Count; i++)
        {
            var index = i;
            var definition = _viewModel.Assistants[i];
            var item = Add(choose, definition.Key, () =>
            {
                _viewModel.GalleryIndex = index;
                _viewModel.UseAssistantCommand.Execute(null);
            });
            item.State = definition == _viewModel.ActiveAssistant ? NSCellStateValue.On : NSCellStateValue.Off;
        }

        menu.AddItem(new NSMenuItem("Choose Assistant") { Submenu = choose, Enabled = _viewModel.Assistants.Count > 0 });
        var sounds = Add(menu, "Make Sounds", () => _viewModel.SoundsEnabled = !_viewModel.SoundsEnabled);
        sounds.State = _viewModel.SoundsEnabled ? NSCellStateValue.On : NSCellStateValue.Off;
        menu.AddItem(NSMenuItem.SeparatorItem);
        Add(menu, "Settings...", () =>
        {
            AppDelegate.ActivateApp();
            _openSettings();
        }).KeyEquivalent = ",";
        Add(menu, "Quit Office Assistant", () => NSApplication.SharedApplication.Terminate(null)).KeyEquivalent = "q";
    }

    private static NSMenuItem Add(NSMenu menu, string title, Action action, bool enabled = true)
    {
        var item = new NSMenuItem(title) { Enabled = enabled };
        item.Activated += (_, _) => action();
        menu.AddItem(item);
        return item;
    }
}
