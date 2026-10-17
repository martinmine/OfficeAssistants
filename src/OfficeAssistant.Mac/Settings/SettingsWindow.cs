using OfficeAssistant.Core;
using OfficeAssistant.Presentation;
using UniformTypeIdentifiers;
using static OfficeAssistant.Mac.Settings.Controls;

namespace OfficeAssistant.Mac.Settings;

/// <summary>
/// The Mac counterpart of the Windows main page: gallery, options, Claude, instructions
/// and tools, and animations, one tab each, on the shared <see cref="MainPageViewModel"/>.
/// Closing it only hides it; the menu bar item and the assistant's menu bring it back.
/// </summary>
internal sealed class SettingsWindow : NSWindow
{
    private static readonly string[] PixelSizes = ["Automatic", "1x (original)", "2x", "3x"];

    private readonly MainPageViewModel _viewModel;
    private readonly AssistantController _assistant;
    private readonly ViewModelBinder _bind;
    private readonly GalleryPreview _preview;
    private readonly NSTabView _tabs = new();

    public SettingsWindow(MainPageViewModel viewModel, AssistantController assistant)
        : base(new CGRect(0, 0, 720, 640), NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable, NSBackingStore.Buffered, false)
    {
        _viewModel = viewModel;
        _assistant = assistant;
        _bind = new ViewModelBinder(viewModel);
        _preview = new GalleryPreview(assistant.Platform);
        Title = "Office Assistant";
        ReleaseWhenClosed(false);
        MinSize = new CGSize(600, 520);
        TabbingMode = NSWindowTabbingMode.Disallowed;

        AddTab("Gallery", GalleryTab());
        AddTab("Options", OptionsTab());
        AddTab("Claude", ClaudeTab());
        AddTab("Instructions and Tools", ProfileTab());
        AddTab("Animations", AnimationsTab());
        ContentView = _tabs;
        Center();

        viewModel.GalleryChanged += (_, _) => RestartPreview();
        WindowShouldClose = _ =>
        {
            // Only hide: the assistant keeps running, and the window comes back as it was.
            _preview.Stop();
            OrderOut(null);
            return false;
        };
    }

    /// <summary>Shows the window in front, on the given tab if one is named.</summary>
    public void Present(string? tab = null)
    {
        if (_tabs.Items.FirstOrDefault(item => item.Label == tab) is { } item)
        {
            _tabs.Select(item);
        }

        AppDelegate.ActivateApp();
        MakeKeyAndOrderFront(null);
        RestartPreview();
    }

    private void AddTab(string label, NSView view) => _tabs.Add(new NSTabViewItem { Label = label, View = view });

    private void RestartPreview()
    {
        if (IsVisible)
        {
            _preview.UpdateScale(_assistant.Scale);
            _preview.Start(_viewModel.GalleryCharacter);
        }
    }

    private NSView GalleryTab()
    {
        var name = Heading(string.Empty);
        var description = Label();
        var previewOnly = Caption("This assistant only has its gallery file (.acg): it can wave, but no more. Add its .acs file to use it fully.");
        var position = Label();
        var mute = Checkbox("Mute preview", value => _viewModel.PreviewMuted = value);

        _bind.On(() => name.StringValue = _viewModel.GalleryName, nameof(_viewModel.GalleryName));
        _bind.On(() => description.StringValue = _viewModel.GalleryDescription, nameof(_viewModel.GalleryDescription));
        _bind.On(() => previewOnly.Hidden = !_viewModel.GalleryIsPreviewOnly, nameof(_viewModel.GalleryIsPreviewOnly));
        _bind.On(() => position.StringValue = _viewModel.GalleryPosition, nameof(_viewModel.GalleryPosition));
        _bind.On(
            () =>
            {
                mute.State = _viewModel.PreviewMuted ? NSCellStateValue.On : NSCellStateValue.Off;
                _preview.Muted = _viewModel.PreviewMuted;
            },
            nameof(_viewModel.PreviewMuted));
        _bind.On(RestartPreview, nameof(_viewModel.ScaleIndex));

        var stage = new NSView { WantsLayer = true };
        stage.Layer!.BackgroundColor = NSColor.ControlBackground.CGColor;
        stage.Layer.CornerRadius = 6;
        stage.AddSubview(_preview);
        stage.HeightAnchor.ConstraintEqualTo(400).Active = true;
        _preview.CenterXAnchor.ConstraintEqualTo(stage.CenterXAnchor).Active = true;
        _preview.CenterYAnchor.ConstraintEqualTo(stage.CenterYAnchor).Active = true;

        return Page(
            Row(Heading("Gallery"), new NSView(), mute),
            stage,
            name,
            description,
            previewOnly,
            Row(Button("< Back", () => _viewModel.PreviousCommand.Execute(null)), position, Button("Next >", () => _viewModel.NextCommand.Execute(null))),
            Row(Button("Use this assistant", () => _viewModel.UseAssistantCommand.Execute(null)), Button("Add assistant files (.acs, .acg)...", AddFiles)));
    }

    private NSView OptionsTab()
    {
        var active = Label();
        var shown = Checkbox("Show the Office Assistant", value => _viewModel.IsAssistantShown = value);
        var sounds = Checkbox("Make sounds", value => _viewModel.SoundsEnabled = value);
        var pixelSize = Popup(PixelSizes, index => _viewModel.ScaleIndex = index);
        var language = Popup([], index => _viewModel.LanguageIndex = index);
        MinWidth(language, 260);

        _bind.On(() => active.StringValue = $"Active assistant: {_viewModel.ActiveName}   {_viewModel.ActiveFiles}", nameof(_viewModel.ActiveName), nameof(_viewModel.ActiveFiles));
        _bind.On(() => shown.State = _viewModel.IsAssistantShown ? NSCellStateValue.On : NSCellStateValue.Off, nameof(_viewModel.IsAssistantShown));
        _bind.On(() => sounds.State = _viewModel.SoundsEnabled ? NSCellStateValue.On : NSCellStateValue.Off, nameof(_viewModel.SoundsEnabled));
        _bind.On(() => pixelSize.SelectItem(_viewModel.ScaleIndex), nameof(_viewModel.ScaleIndex));
        ViewModelBinder.OnCollection(_viewModel.Languages, () =>
        {
            language.RemoveAllItems();
            language.AddItems([.. _viewModel.Languages.Select(l => l.DisplayName)]);
            language.SelectItem(_viewModel.LanguageIndex);
        });
        _bind.On(() => language.SelectItem(_viewModel.LanguageIndex), nameof(_viewModel.LanguageIndex));

        return Page(
            Heading("Options"),
            active,
            shown,
            sounds,
            Row(Label("Pixel size:"), pixelSize),
            Row(Label("Assistant language:"), language),
            Caption("Click the assistant to ask a question, drag it to move it, double-click to animate, right-click (or Control-click) for its menu."));
    }

    private NSView ClaudeTab()
    {
        var key = new NSSecureTextField { PlaceholderString = "sk-ant-..." };
        MinWidth(key, 320);
        key.Changed += (_, _) => _viewModel.ApiKeyInput = key.StringValue;
        var status = Caption();
        var removeKey = Button("Remove key", () => _viewModel.ClearApiKeyCommand.Execute(null));
        var model = new NSComboBox { Completes = true };
        model.Add([.. _viewModel.Models.Select(m => (NSString)m)]);
        MinWidth(model, 260);
        model.Changed += (_, _) => _viewModel.Model = model.StringValue;
        model.SelectionChanged += (_, _) =>
        {
            if (model.SelectedIndex >= 0)
            {
                _viewModel.Model = _viewModel.Models[(int)model.SelectedIndex];
            }
        };

        _bind.On(() => key.StringValue = _viewModel.ApiKeyInput, nameof(_viewModel.ApiKeyInput));
        _bind.On(() => status.StringValue = _viewModel.ApiKeyStatus, nameof(_viewModel.ApiKeyStatus));
        _bind.On(() => removeKey.Enabled = _viewModel.HasStoredApiKey, nameof(_viewModel.HasStoredApiKey));
        _bind.On(
            () =>
            {
                if (model.StringValue != _viewModel.Model)
                {
                    model.StringValue = _viewModel.Model;
                }
            },
            nameof(_viewModel.Model));

        return Page(
            Row(Heading("Claude"), new NSView(), Button("Ask the assistant", () => _viewModel.AskAssistantCommand.Execute(null)), Button("New conversation", () => _viewModel.NewConversationCommand.Execute(null))),
            Caption("The assistant answers with Claude. Click it, type a question and click Ask; it remembers the conversation until you start a new topic."),
            Label("API key:"),
            Row(key, Button("Save key", () => _viewModel.SaveApiKeyCommand.Execute(null)), removeKey),
            status,
            Row(Label("Model:"), model));
    }

    private NSView ProfileTab()
    {
        var heading = Heading(string.Empty);
        var (instructionsScroll, instructions) = TextEditor(90, monospaced: false);
        var (serversScroll, servers) = TextEditor(150, monospaced: true);
        var error = Label();
        error.TextColor = NSColor.SystemRed;
        var status = Caption();
        var serverList = Label();
        var settingsPath = Caption($"Settings file: {_viewModel.SettingsPath}");
        settingsPath.Selectable = true;

        instructions.TextDidChange += (_, _) => _viewModel.Instructions = instructions.Value;
        servers.TextDidChange += (_, _) => _viewModel.McpServersJson = servers.Value;
        _bind.On(() => heading.StringValue = $"Instructions and tools for {_viewModel.ActiveName}", nameof(_viewModel.ActiveName));
        _bind.On(() => SetText(instructions, _viewModel.Instructions), nameof(_viewModel.Instructions));
        _bind.On(() => SetText(servers, _viewModel.McpServersJson), nameof(_viewModel.McpServersJson));
        _bind.On(
            () =>
            {
                error.StringValue = _viewModel.McpError;
                error.Hidden = !_viewModel.HasMcpError;
            },
            nameof(_viewModel.McpError));
        _bind.On(() => status.StringValue = _viewModel.ProfileStatus, nameof(_viewModel.ProfileStatus));
        ViewModelBinder.OnCollection(_viewModel.McpServers, () =>
            serverList.StringValue = string.Join("\n", _viewModel.McpServers.Select(s =>
                $"{Symbol(s.Status)} {s.Name}: {s.Summary}{(s.Tools.Length > 0 ? $"\n     {s.Tools}" : string.Empty)}")));

        return Page(
            Row(heading, new NSView(), Button("Reconnect", () => _viewModel.ReconnectMcpCommand.Execute(null))),
            Label("Instructions:"),
            instructionsScroll,
            Caption("Optional. For example: I work in the finance team, so prefer Excel examples."),
            Label("MCP servers (JSON):"),
            serversScroll,
            Caption("Same format as Claude Desktop's mcpServers: \"command\" with \"args\", \"env\" and \"cwd\" for a local server, or \"url\" with \"headers\" for a remote one. The assistant asks before each tool call; add \"alwaysAllow\": [\"tool_name\"] (or [\"*\"]) to skip asking, or \"disabled\": true to turn a server off."),
            error,
            Row(Button("Save and connect", () => _viewModel.SaveProfileCommand.Execute(null)), status),
            serverList,
            settingsPath);
    }

    private NSView AnimationsTab()
    {
        var playing = Label();
        _bind.On(() => playing.StringValue = $"Playing: {_viewModel.CurrentAnimation}", nameof(_viewModel.CurrentAnimation));
        var list = new AnimationList(_viewModel.Animations, name => _viewModel.PlayAnimationCommand.Execute(name));
        return Page(
            Row(Heading("Animations"), new NSView(), playing, Button("Animate!", () => _viewModel.AnimateCommand.Execute(null))),
            list.View);
    }

    private void AddFiles()
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.AllowsMultipleSelection = true;
        panel.CanChooseDirectories = false;
        panel.AllowedContentTypes = [.. new[] { "acs", "acg" }.Select(e => UTType.CreateFromExtension(e)).OfType<UTType>()];
        panel.BeginSheet(this, result =>
        {
            if (result == (nint)(long)NSModalResponse.OK)
            {
                _viewModel.AddFiles(panel.Urls.Select(u => u.Path).OfType<string>());
            }
        });
    }

    private static void SetText(NSTextView view, string text)
    {
        if (view.Value != text)
        {
            view.Value = text;
        }
    }

    private static string Symbol(McpServerStatus status) => status switch
    {
        McpServerStatus.Success => "🟢",
        McpServerStatus.Warning => "🟡",
        McpServerStatus.Error => "🔴",
        _ => "⚪️",
    };
}
