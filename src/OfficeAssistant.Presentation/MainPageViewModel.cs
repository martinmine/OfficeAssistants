using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using AcsFormat;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OfficeAssistant.Core;
using OfficeAssistant.Core.Chat;

namespace OfficeAssistant.Presentation;

public sealed record LanguageOption(int Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>How an MCP server's connection is going, for colouring its status.</summary>
public enum McpServerStatus
{
    Informational,
    Success,
    Warning,
    Error,
}

/// <summary>One configured MCP server and how its connection is going.</summary>
public sealed record McpServerItem(string Name, string Summary, McpServerStatus Status, string Tools);

/// <summary>
/// State for the gallery (choosing an assistant, like Office's "Gallery" tab), the
/// assistant's options, the Claude connection and the assistant's MCP servers.
/// </summary>
public partial class MainPageViewModel : ObservableObject
{
    private readonly AssistantController _assistant;
    private readonly SettingsStore _settings;
    private readonly Dictionary<string, AcsCharacter> _galleryCache = [];
    private IReadOnlyList<AssistantDefinition> _assistants = [];
    private bool _updatingLanguage;

    public MainPageViewModel(AssistantController assistant, SettingsStore settings)
    {
        _assistant = assistant;
        _settings = settings;
        _assistant.VisibilityChanged += (_, _) => IsAssistantShown = _assistant.IsShown;
        _assistant.AnimationStarted += (_, name) => CurrentAnimation = name;
        _assistant.Mcp.Changed += (_, _) => RefreshMcpServers();
        Model = _settings.Settings.Model;
        ScaleIndex = _settings.Settings.PixelSize ?? 0;
        PreviewMuted = _settings.Settings.PreviewMuted;
        RefreshApiKeyStatus();
    }

    /// <summary>Raised when the gallery shows a different assistant (the page restarts the preview).</summary>
    public event EventHandler? GalleryChanged;

    public ObservableCollection<LanguageOption> Languages { get; } = [];

    public ObservableCollection<string> Animations { get; } = [];

    public ObservableCollection<McpServerItem> McpServers { get; } = [];

    /// <summary>Suggestions for the model box; any model id can be typed.</summary>
    public IReadOnlyList<string> Models { get; } = ["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5", "claude-fable-5-1"];

    [ObservableProperty]
    public partial int GalleryIndex { get; set; } = -1;

    [ObservableProperty]
    public partial AcsCharacter? GalleryCharacter { get; set; }

    [ObservableProperty]
    public partial string GalleryName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GalleryDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GalleryPosition { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool GalleryIsPreviewOnly { get; set; }

    [ObservableProperty]
    public partial string ActiveName { get; set; } = "None";

    [ObservableProperty]
    public partial string ActiveFiles { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ActiveIsPreviewOnly { get; set; }

    [ObservableProperty]
    public partial string CurrentAnimation { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsAssistantShown { get; set; }

    [ObservableProperty]
    public partial bool SoundsEnabled { get; set; } = true;

    /// <summary>Silences the gallery preview only; the assistant keeps "Make sounds".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewMuteGlyph), nameof(PreviewMuteLabel))]
    public partial bool PreviewMuted { get; set; }

    /// <summary>Segoe Fluent Icons: Mute or Volume.</summary>
    public string PreviewMuteGlyph => PreviewMuted ? "" : "";

    public string PreviewMuteLabel => PreviewMuted ? "Unmute preview" : "Mute preview";

    /// <summary>0 = automatic (from the display scaling), 1 = original pixels, 2 = 2x, 3 = 3x.</summary>
    [ObservableProperty]
    public partial int ScaleIndex { get; set; }

    [ObservableProperty]
    public partial int LanguageIndex { get; set; } = -1;

    /// <summary>A new API key typed by the user; never filled with the stored key.</summary>
    [ObservableProperty]
    public partial string ApiKeyInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ApiKeyStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasStoredApiKey { get; set; }

    [ObservableProperty]
    public partial string Model { get; set; } = AppSettings.DefaultModel;

    [ObservableProperty]
    public partial string Instructions { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string McpServersJson { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMcpError))]
    public partial string McpError { get; set; } = string.Empty;

    public bool HasMcpError => McpError.Length > 0;

    [ObservableProperty]
    public partial string ProfileStatus { get; set; } = string.Empty;

    public bool HasAssistants => _assistants.Count > 0;

    public string SettingsPath => _settings.Path;

    /// <summary>Loads assistants from the folders and shows the first one (Clippit if present).</summary>
    public void Initialize(IEnumerable<string> folders)
    {
        SetAssistants(AssistantCatalog.Scan(folders));
        if (_assistants.Count == 0)
        {
            return;
        }

        var clippit = _assistants.ToList().FindIndex(a => a.Key == "CLIPPIT");
        GalleryIndex = Math.Max(0, clippit);
        UseAssistant();
    }

    public void AddFiles(IEnumerable<string> files)
    {
        var current = GalleryIndex >= 0 ? _assistants[GalleryIndex].Key : null;
        SetAssistants(AssistantCatalog.Merge(_assistants, files));
        var index = _assistants.ToList().FindIndex(a => a.Key == current);
        GalleryIndex = Math.Max(0, index);
        OnGalleryIndexChanged(GalleryIndex);
    }

    [RelayCommand]
    private void Previous() => GalleryIndex = (GalleryIndex - 1 + _assistants.Count) % _assistants.Count;

    [RelayCommand]
    private void Next() => GalleryIndex = (GalleryIndex + 1) % _assistants.Count;

    /// <summary>Makes the assistant shown in the gallery the active one (the gallery's OK).</summary>
    [RelayCommand]
    private void UseAssistant()
    {
        if (GalleryIndex < 0)
        {
            return;
        }

        var definition = _assistants[GalleryIndex];
        _assistant.Load(definition);
        if (!_assistant.IsShown)
        {
            _assistant.Show();
        }

        IsAssistantShown = _assistant.IsShown;
        ActiveIsPreviewOnly = !definition.HasFullCharacter;
        ActiveFiles = string.Join(" + ", new[] { definition.CharacterPath, definition.GalleryPath }.Where(p => p is not null).Select(Path.GetFileName));

        Animations.Clear();
        foreach (var animation in _assistant.Character!.Animations.Select(a => a.Name).Order(StringComparer.OrdinalIgnoreCase))
        {
            Animations.Add(animation);
        }

        RefreshLanguages(_assistant.Character);
        RefreshActiveName();
        LoadProfile();
    }

    [RelayCommand]
    private void Animate() => _assistant.Animate();

    [RelayCommand]
    private void PlayAnimation(string? name)
    {
        if (name is null)
        {
            return;
        }

        if (!_assistant.IsShown)
        {
            _assistant.Show(greet: false);
        }

        _assistant.Play(name);
    }

    /// <summary>Opens "What would you like to do?" on the assistant.</summary>
    [RelayCommand]
    private void AskAssistant()
    {
        if (!_assistant.IsShown)
        {
            _assistant.Show(greet: false);
        }

        _assistant.ShowAskBalloon();
    }

    [RelayCommand]
    private void NewConversation()
    {
        _assistant.NewConversation();
        _assistant.CloseBalloon();
        ProfileStatus = "Started a new conversation.";
    }

    [RelayCommand]
    private void SaveApiKey()
    {
        if (string.IsNullOrWhiteSpace(ApiKeyInput))
        {
            return;
        }

        _settings.ApiKey = ApiKeyInput;
        _settings.Save();
        ApiKeyInput = string.Empty;
        RefreshApiKeyStatus();
    }

    [RelayCommand]
    private void ClearApiKey()
    {
        _settings.ApiKey = null;
        _settings.Save();
        RefreshApiKeyStatus();
    }

    /// <summary>Saves the instructions and MCP servers of the active assistant and reconnects.</summary>
    [RelayCommand]
    private async Task SaveProfile()
    {
        if (_assistant.Profile is not { } profile)
        {
            return;
        }

        Dictionary<string, McpServerConfig> servers;
        try
        {
            servers = SettingsStore.ParseMcpServers(McpServersJson);
        }
        catch (JsonException e)
        {
            McpError = $"The MCP servers aren't valid JSON: {e.Message}";
            return;
        }

        McpError = string.Empty;
        profile.Instructions = string.IsNullOrWhiteSpace(Instructions) ? null : Instructions.Trim();
        profile.McpServers = servers;
        _settings.Save();
        McpServersJson = servers.Count == 0 ? string.Empty : SettingsStore.FormatMcpServers(servers);

        // New instructions and tools apply to a fresh conversation.
        _assistant.NewConversation();
        ProfileStatus = "Saved. Connecting to the MCP servers...";
        await _assistant.ConnectMcpServersAsync();
        ProfileStatus = "Saved.";
    }

    [RelayCommand]
    private async Task ReconnectMcp()
    {
        ProfileStatus = "Connecting to the MCP servers...";
        await _assistant.ConnectMcpServersAsync();
        ProfileStatus = string.Empty;
    }

    partial void OnModelChanged(string value)
    {
        var model = value.Trim();
        if (model.Length == 0 || model == _settings.Settings.Model)
        {
            return;
        }

        _settings.Settings.Model = model;
        _settings.Save();
    }

    partial void OnGalleryIndexChanged(int value)
    {
        if (value < 0 || value >= _assistants.Count)
        {
            return;
        }

        var definition = _assistants[value];
        if (!_galleryCache.TryGetValue(definition.Key, out var gallery))
        {
            gallery = definition.LoadGallery();
            _galleryCache[definition.Key] = gallery;
        }

        GalleryCharacter = gallery;
        GalleryIsPreviewOnly = !definition.HasFullCharacter;
        GalleryPosition = $"{value + 1} of {_assistants.Count}";
        RefreshGalleryText();
        GalleryChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsAssistantShownChanged(bool value)
    {
        if (value == _assistant.IsShown || _assistant.Character is null)
        {
            return;
        }

        if (value)
        {
            _assistant.Show(greet: false);
        }
        else
        {
            _assistant.Hide();
        }
    }

    partial void OnSoundsEnabledChanged(bool value) => _assistant.SoundsEnabled = value;

    partial void OnPreviewMutedChanged(bool value)
    {
        if (value != _settings.Settings.PreviewMuted)
        {
            _settings.Settings.PreviewMuted = value;
            _settings.Save();
        }
    }

    partial void OnScaleIndexChanged(int value)
    {
        int? pixelSize = value > 0 ? value : null;
        _assistant.PreferredScale = pixelSize;
        if (pixelSize != _settings.Settings.PixelSize)
        {
            _settings.Settings.PixelSize = pixelSize;
            _settings.Save();
        }
    }

    partial void OnLanguageIndexChanged(int value)
    {
        if (value < 0 || value >= Languages.Count || _updatingLanguage)
        {
            return;
        }

        _assistant.LanguageId = Languages[value].Id;
        RefreshActiveName();
        RefreshGalleryText();
    }

    private void SetAssistants(IReadOnlyList<AssistantDefinition> assistants)
    {
        _assistants = assistants;
        OnPropertyChanged(nameof(HasAssistants));
    }

    private void LoadProfile()
    {
        var profile = _assistant.Profile;
        Instructions = profile?.Instructions ?? string.Empty;
        McpServersJson = profile is { McpServers.Count: > 0 } ? SettingsStore.FormatMcpServers(profile.McpServers) : string.Empty;
        McpError = string.Empty;
        ProfileStatus = string.Empty;
        RefreshMcpServers();
    }

    private void RefreshMcpServers()
    {
        McpServers.Clear();
        foreach (var server in _assistant.Mcp.Servers)
        {
            var status = server.State switch
            {
                McpServerState.Connected => McpServerStatus.Success,
                McpServerState.Failed => McpServerStatus.Error,
                McpServerState.Disabled => McpServerStatus.Warning,
                _ => McpServerStatus.Informational,
            };
            McpServers.Add(new McpServerItem(server.Name, server.Summary, status, string.Join(", ", server.Tools.Select(t => t.Tool.Name))));
        }
    }

    private void RefreshApiKeyStatus()
    {
        HasStoredApiKey = _settings.ApiKey is not null;
        ApiKeyStatus = HasStoredApiKey
            ? $"An API key is saved, {_assistant.Platform.Secrets.Description}."
            : !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))
                ? "Using the key from the ANTHROPIC_API_KEY environment variable."
                : "No key saved. Paste a Claude API key from platform.claude.com, or set the ANTHROPIC_API_KEY environment variable.";
    }

    private void RefreshGalleryText()
    {
        var info = GalleryCharacter?.LocalizedInfo.Count > 0 ? GalleryCharacter.GetLocalizedInfo(_assistant.LanguageId) : null;
        GalleryName = info?.Name ?? (GalleryIndex >= 0 ? _assistants[GalleryIndex].Key : string.Empty);
        GalleryDescription = info?.Description ?? string.Empty;
    }

    private void RefreshLanguages(AcsCharacter character)
    {
        _updatingLanguage = true;
        Languages.Clear();
        foreach (var info in character.LocalizedInfo.OrderBy(l => LanguageName(l.LanguageId), StringComparer.CurrentCulture))
        {
            Languages.Add(new LanguageOption(info.LanguageId, LanguageName(info.LanguageId)));
        }

        var current = character.LocalizedInfo.Count > 0 ? character.GetLocalizedInfo(_assistant.LanguageId).LanguageId : -1;
        LanguageIndex = Languages.ToList().FindIndex(l => l.Id == current);
        _updatingLanguage = false;
    }

    private void RefreshActiveName() =>
        ActiveName = _assistant.Strings?.Name ?? _assistant.Definition?.Key ?? "None";

    private static string LanguageName(int languageId)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(languageId);
            return $"{culture.DisplayName} ({culture.Name})";
        }
        catch (CultureNotFoundException)
        {
            return $"Language 0x{languageId:X4}";
        }
    }
}
