using System.Text.Json;
using System.Text.Json.Serialization;
using OfficeAssistant.Core.Platform;

namespace OfficeAssistant.Core.Chat;

/// <summary>Settings for the Claude connection and every assistant's profile.</summary>
public sealed class AppSettings
{
    public const string DefaultModel = "claude-opus-5-5";

    public string Model { get; set; } = DefaultModel;

    /// <summary>Pixel size (1 to 4) the user picked, or null to follow the display's scaling.</summary>
    public int? PixelSize { get; set; }

    /// <summary>Silences the gallery preview only; the assistant keeps its own sound setting.</summary>
    public bool PreviewMuted { get; set; }

    /// <summary>The API key as protected by the platform's <see cref="ISecretStore"/> (encrypted with DPAPI on Windows, a Keychain reference on macOS).</summary>
    public string? ProtectedApiKey { get; set; }

    /// <summary>Profiles keyed by assistant file name, e.g. CLIPPIT.</summary>
    public Dictionary<string, AssistantProfile> Assistants { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Properties this version doesn't know about, kept so saving doesn't drop them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Other { get; set; }

    public AssistantProfile GetProfile(string key)
    {
        if (!Assistants.TryGetValue(key, out var profile))
        {
            profile = new AssistantProfile();
            Assistants[key] = profile;
        }

        return profile;
    }
}

/// <summary>What one assistant knows: extra instructions and the MCP servers it can use.</summary>
public sealed class AssistantProfile
{
    /// <summary>Extra personality or behaviour instructions added to the system prompt.</summary>
    public string? Instructions { get; set; }

    /// <summary>MCP servers by name, in the same shape as Claude Desktop's <c>mcpServers</c>.</summary>
    public Dictionary<string, McpServerConfig> McpServers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// One MCP server. Set <see cref="Command"/> for a local (stdio) server or <see cref="Url"/>
/// for a remote (Streamable HTTP or SSE) one.
/// </summary>
public sealed class McpServerConfig
{
    public string? Command { get; set; }

    public List<string>? Args { get; set; }

    public Dictionary<string, string>? Env { get; set; }

    public string? Cwd { get; set; }

    public string? Url { get; set; }

    public Dictionary<string, string>? Headers { get; set; }

    public bool Disabled { get; set; }

    /// <summary>Tools that run without asking first; "*" allows every tool of the server.</summary>
    public List<string>? AlwaysAllow { get; set; }

    public bool IsAlwaysAllowed(string tool) =>
        AlwaysAllow?.Any(t => t == "*" || string.Equals(t, tool, StringComparison.OrdinalIgnoreCase)) == true;
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Dictionary<string, McpServerConfig>))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON in the user's local app data.</summary>
public sealed class SettingsStore
{
    private readonly ISecretStore? _secrets;

    /// <param name="path">The settings file; null for the default in local app data.</param>
    /// <param name="secrets">Protects the API key. Without it no key can be stored.</param>
    public SettingsStore(string? path = null, ISecretStore? secrets = null)
    {
        _secrets = secrets;
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OfficeAssistant", "settings.json");
        Settings = Load(Path);
    }

    public string Path { get; }

    public AppSettings Settings { get; }

    /// <summary>Whether settings were ever saved, i.e. the user has set something up.</summary>
    public bool HasSavedSettings => File.Exists(Path);

    /// <summary>Raised after <see cref="Save"/>.</summary>
    public event EventHandler? Saved;

    /// <summary>The stored API key, or null to let the SDK find one (ANTHROPIC_API_KEY, ant auth profile).</summary>
    public string? ApiKey
    {
        get
        {
            return string.IsNullOrEmpty(Settings.ProtectedApiKey) ? null : _secrets?.Unprotect(Settings.ProtectedApiKey);
        }

        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (!string.IsNullOrEmpty(Settings.ProtectedApiKey))
                {
                    _secrets?.Remove(Settings.ProtectedApiKey);
                }

                Settings.ProtectedApiKey = null;
                return;
            }

            if (_secrets is null)
            {
                throw new InvalidOperationException("This settings store has no secret store to protect the API key with.");
            }

            Settings.ProtectedApiKey = _secrets.Protect(value.Trim());
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(Settings, SettingsJsonContext.Default.AppSettings));
        Saved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Parses an <c>mcpServers</c> object, or a whole file that contains one.</summary>
    public static Dictionary<string, McpServerConfig> ParseMcpServers(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }

        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var document = JsonDocument.Parse(json, options);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("mcpServers", out var inner))
        {
            root = inner;
        }

        var servers = root.Deserialize(SettingsJsonContext.Default.DictionaryStringMcpServerConfig)
            ?? throw new JsonException("Expected an object of MCP servers.");
        foreach (var (name, server) in servers)
        {
            if (string.IsNullOrWhiteSpace(server.Command) == string.IsNullOrWhiteSpace(server.Url))
            {
                throw new JsonException($"MCP server \"{name}\" needs either a \"command\" or a \"url\".");
            }
        }

        return new(servers, StringComparer.OrdinalIgnoreCase);
    }

    public static string FormatMcpServers(IReadOnlyDictionary<string, McpServerConfig> servers) =>
        JsonSerializer.Serialize(servers.ToDictionary(), SettingsJsonContext.Default.DictionaryStringMcpServerConfig);

    private static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJsonContext.Default.AppSettings);
                if (settings is not null)
                {
                    settings.Assistants = new(settings.Assistants, StringComparer.OrdinalIgnoreCase);
                    return settings;
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A broken file falls back to defaults; saving rewrites it.
        }

        return new AppSettings();
    }
}
