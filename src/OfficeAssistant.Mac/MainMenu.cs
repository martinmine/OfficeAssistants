namespace OfficeAssistant.Mac;

/// <summary>The app's menu bar: the application menu and Edit (so text fields get copy and paste).</summary>
internal static class MainMenu
{
    public static NSMenu Create()
    {
        var menu = new NSMenu();

        var app = new NSMenu("Office Assistant");
        app.AddItem(new NSMenuItem("About Office Assistant", new ObjCRuntime.Selector("orderFrontStandardAboutPanel:"), string.Empty));
        app.AddItem(NSMenuItem.SeparatorItem);
        app.AddItem(new NSMenuItem("Hide Office Assistant", new ObjCRuntime.Selector("hide:"), "h"));
        app.AddItem(NSMenuItem.SeparatorItem);
        app.AddItem(new NSMenuItem("Quit Office Assistant", new ObjCRuntime.Selector("terminate:"), "q"));
        menu.AddItem(new NSMenuItem("Office Assistant") { Submenu = app });

        var edit = new NSMenu("Edit");
        edit.AddItem(new NSMenuItem("Undo", new ObjCRuntime.Selector("undo:"), "z"));
        edit.AddItem(new NSMenuItem("Redo", new ObjCRuntime.Selector("redo:"), "Z"));
        edit.AddItem(NSMenuItem.SeparatorItem);
        edit.AddItem(new NSMenuItem("Cut", new ObjCRuntime.Selector("cut:"), "x"));
        edit.AddItem(new NSMenuItem("Copy", new ObjCRuntime.Selector("copy:"), "c"));
        edit.AddItem(new NSMenuItem("Paste", new ObjCRuntime.Selector("paste:"), "v"));
        edit.AddItem(new NSMenuItem("Select All", new ObjCRuntime.Selector("selectAll:"), "a"));
        menu.AddItem(new NSMenuItem("Edit") { Submenu = edit });

        return menu;
    }
}
