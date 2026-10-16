using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace OfficeAssistant.Core.Chat;

public enum McpServerState
{
    Disabled,
    Connecting,
    Connected,
    Failed,
}

/// <summary>A tool offered by a connected MCP server, under a name that is unique across servers.</summary>
public sealed record McpTool(string Name, string Server, McpServerConfig Config, McpClientTool Tool)
{
    public string Description => Tool.Description;

    public JsonElement InputSchema => Tool.JsonSchema;
}

/// <summary>Connection status of one configured server.</summary>
public sealed class McpServerStatus
{
    internal McpServerStatus(string name, McpServerConfig config)
    {
        Name = name;
        Config = config;
    }

    public string Name { get; }

    public McpServerConfig Config { get; }

    public McpServerState State { get; internal set; }

    public string? Error { get; internal set; }

    public IReadOnlyList<McpTool> Tools { get; internal set; } = [];

    internal McpClient? Client { get; set; }

    public string Summary => State switch
    {
        McpServerState.Disabled => "Disabled",
        McpServerState.Connecting => "Connecting...",
        McpServerState.Connected => Tools.Count == 1 ? "Connected, 1 tool" : $"Connected, {Tools.Count} tools",
        _ => $"Failed: {Error}",
    };
}

/// <summary>
/// Connects to the MCP servers configured for the active assistant and lists their tools.
/// Use it from the UI thread; <see cref="Changed"/> is raised there too.
/// </summary>
public sealed class McpHub : IAsyncDisposable
{
    private const int MaxToolNameLength = 64;

    private List<McpServerStatus> _servers = [];
    private int _generation;

    /// <summary>Raised when a server connects, fails or the set of servers changes.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<McpServerStatus> Servers => _servers;

    public IEnumerable<McpTool> Tools => _servers.Where(s => s.State == McpServerState.Connected).SelectMany(s => s.Tools);

    /// <summary>Disconnects from the current servers and connects to <paramref name="servers"/>.</summary>
    public async Task ConnectAsync(IReadOnlyDictionary<string, McpServerConfig> servers)
    {
        var generation = ++_generation;
        var previous = _servers;
        _servers = servers.Select(s => new McpServerStatus(s.Key, s.Value)
        {
            State = s.Value.Disabled ? McpServerState.Disabled : McpServerState.Connecting,
        }).ToList();
        Changed?.Invoke(this, EventArgs.Empty);

        await DisconnectAsync(previous);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        await Task.WhenAll(_servers.Where(s => s.State == McpServerState.Connecting).Select(s => ConnectOneAsync(s, generation, usedNames)));
    }

    public async ValueTask DisposeAsync()
    {
        _generation++;
        var servers = _servers;
        _servers = [];
        await DisconnectAsync(servers);
    }

    /// <summary>Turns MCP content into text for a tool result.</summary>
    public static string FormatResult(CallToolResult result)
    {
        var text = new StringBuilder();
        foreach (var block in result.Content)
        {
            if (text.Length > 0)
            {
                text.AppendLine();
            }

            text.Append(block switch
            {
                TextContentBlock t => t.Text,
                ImageContentBlock i => $"[image: {i.MimeType}]",
                AudioContentBlock a => $"[audio: {a.MimeType}]",
                EmbeddedResourceBlock { Resource: TextResourceContents r } => r.Text,
                EmbeddedResourceBlock e => $"[resource: {e.Resource.Uri}]",
                ResourceLinkBlock l => $"[resource link: {l.Uri}]",
                _ => $"[{block.Type} content]",
            });
        }

        if (text.Length == 0 && result.StructuredContent is { } structured)
        {
            text.Append(structured.GetRawText());
        }

        return text.Length == 0 ? "(no output)" : text.ToString();
    }

    private async Task ConnectOneAsync(McpServerStatus status, int generation, HashSet<string> usedNames)
    {
        McpClient? client = null;
        try
        {
            client = await McpClient.CreateAsync(CreateTransport(status.Name, status.Config));
            var tools = await client.ListToolsAsync();
            if (generation != _generation)
            {
                await client.DisposeAsync();
                return;
            }

            status.Client = client;
            status.Tools = tools.Select(t => new McpTool(UniqueName(status.Name, t.Name, usedNames), status.Name, status.Config, t)).ToList();
            status.State = McpServerState.Connected;
        }
        catch (Exception e)
        {
            if (client is not null)
            {
                await client.DisposeAsync();
            }

            if (generation != _generation)
            {
                return;
            }

            status.State = McpServerState.Failed;
            status.Error = e.GetBaseException().Message;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static IClientTransport CreateTransport(string name, McpServerConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.Url))
        {
            return new HttpClientTransport(new HttpClientTransportOptions
            {
                Name = name,
                Endpoint = new Uri(config.Url),
                AdditionalHeaders = config.Headers,
            });
        }

        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = name,
            Command = config.Command!,
            Arguments = config.Args ?? [],
            EnvironmentVariables = config.Env?.ToDictionary(e => e.Key, e => (string?)e.Value),
            WorkingDirectory = string.IsNullOrWhiteSpace(config.Cwd) ? null : config.Cwd,
        });
    }

    /// <summary>"server__tool", limited to the characters and length Claude accepts.</summary>
    private static string UniqueName(string server, string tool, HashSet<string> used)
    {
        static string Clean(string s) => new([.. s.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_')]);

        var name = $"{Clean(server)}__{Clean(tool)}";
        if (name.Length > MaxToolNameLength)
        {
            name = name[..MaxToolNameLength];
        }

        var unique = name;
        for (var i = 2; !used.Add(unique); i++)
        {
            var suffix = $"_{i}";
            unique = name[..Math.Min(name.Length, MaxToolNameLength - suffix.Length)] + suffix;
        }

        return unique;
    }

    private static async Task DisconnectAsync(IEnumerable<McpServerStatus> servers)
    {
        foreach (var server in servers)
        {
            if (server.Client is { } client)
            {
                server.Client = null;
                try
                {
                    await client.DisposeAsync();
                }
                catch (Exception)
                {
                    // The server may already be gone; there is nothing left to clean up.
                }
            }
        }
    }
}
