using OfficeAssistant.Core;
using OfficeAssistant.Core.Chat;
using OfficeAssistant.Mac.Platform;
using OfficeAssistant.Presentation;

namespace OfficeAssistant.Mac;

/// <summary>
/// Starts the assistant: the floating character and balloon, a menu bar item to control
/// it, and the settings window.
/// </summary>
[Register("AppDelegate")]
public sealed class AppDelegate : NSApplicationDelegate
{
    private AssistantController? _assistant;
    private StatusMenu? _statusMenu;

    public static SettingsStore Settings { get; private set; } = null!;

    public static AssistantController Assistant { get; private set; } = null!;

    public static MainPageViewModel ViewModel { get; private set; } = null!;

    public override void DidFinishLaunching(NSNotification notification)
    {
        LoginShellEnvironment.ApplyPath();
        NSApplication.SharedApplication.MainMenu = MainMenu.Create();

        var platform = new MacPlatform();
        Settings = new SettingsStore(secrets: platform.Secrets);
        Assistant = _assistant = new AssistantController(platform, Settings);
        ViewModel = new MainPageViewModel(Assistant, Settings);

        var folder = Path.Combine(NSBundle.MainBundle.ResourcePath ?? AppContext.BaseDirectory, "Assistants");
        ViewModel.Initialize([folder]);
        _statusMenu = new StatusMenu(ViewModel, Assistant, OpenSettings);
        Assistant.OptionsRequested += (_, _) => OpenSettings();
        Assistant.ChooseAssistantRequested += (_, _) => OpenSettings();

        // Open the settings only until the user has saved settings, or when no assistant is on screen to open them from.
        if (!Settings.HasSavedSettings || !Assistant.IsOnScreen)
        {
            OpenSettings();
        }
    }

    // The assistant lives on after its windows close; Quit is in the menus.
    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => false;

    public override bool ApplicationShouldHandleReopen(NSApplication sender, bool hasVisibleWindows)
    {
        // Clicking the Dock icon brings the settings back.
        if (!hasVisibleWindows)
        {
            OpenSettings();
        }

        return true;
    }

    public override void WillTerminate(NSNotification notification) => _assistant?.Dispose();

    private void OpenSettings()
    {
        // The settings window arrives in the next step; until then the menu bar item is the UI.
        _statusMenu?.Open();
    }
}
