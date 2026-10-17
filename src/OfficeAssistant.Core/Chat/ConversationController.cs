using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using OfficeAssistant.Core.Animation;
using OfficeAssistant.Core.Rendering;

namespace OfficeAssistant.Core.Chat;

/// <summary>
/// The conversation with Claude behind the balloon: the client, the chat, the running
/// request, tool approval and the pages of the answer. Create and use it on the UI thread.
/// </summary>
internal sealed class ConversationController : IChatHost, IDisposable
{
    private readonly SettingsStore _settings;
    private readonly CharacterPresenter _character;
    private readonly BalloonCoordinator _balloon;
    private readonly AnswerPager _pager = new();

    private AssistantChat? _chat;
    private AnthropicClient? _client;
    private string? _clientKey;
    private CancellationTokenSource? _request;
    private TaskCompletionSource<ToolApproval>? _approval;
    private string _status = string.Empty;
    private bool _animatedThisTurn;

    public ConversationController(SettingsStore settings, CharacterPresenter character, BalloonCoordinator balloon)
    {
        _settings = settings;
        _character = character;
        _balloon = balloon;
        _settings.Saved += (_, _) => _client = null;
    }

    /// <summary>The MCP servers of the active assistant.</summary>
    public McpHub Mcp { get; } = new();

    /// <summary>The assistant the user is talking to.</summary>
    public AssistantDefinition? Definition { get; set; }

    /// <summary>The active assistant's instructions and MCP servers.</summary>
    public AssistantProfile? Profile => Definition is null ? null : _settings.Settings.GetProfile(Definition.Key);

    public bool IsBusy => _chat?.IsBusy == true;

    public bool HasHistory => _chat?.HasHistory == true;

    /// <summary>True while a tool call waits for the user's Allow or Deny.</summary>
    public bool IsAwaitingApproval => _approval is not null;

    /// <summary>Reconnects to the active assistant's MCP servers after they were edited.</summary>
    public Task ConnectMcpServersAsync() =>
        Mcp.ConnectAsync(Profile?.McpServers ?? new Dictionary<string, McpServerConfig>());

    /// <summary>Drops the chat so the next question builds a new one (for a new assistant).</summary>
    public void ResetChat() => _chat = null;

    /// <summary>Forgets the conversation so the next question starts fresh (and picks up new instructions).</summary>
    public void NewConversation()
    {
        CancelRequest();
        _chat = null;
        _pager.Clear();
    }

    /// <summary>Shows what the assistant is busy with.</summary>
    public void ShowStatus() => _balloon.Show(ChatBalloons.Working(_status), activate: false);

    public async void Ask(string question)
    {
        if (string.IsNullOrWhiteSpace(question) || Definition is null || IsBusy)
        {
            return;
        }

        _chat ??= CreateChat();
        _animatedThisTurn = false;
        _request = new CancellationTokenSource();
        var cancellation = _request.Token;
        try
        {
            var reply = await _chat.AskAsync(GetClient(), _settings.Settings.Model, question.Trim(), this, cancellation);
            if (reply.IsError)
            {
                _character.Play("Alert");
                _balloon.Show(ChatBalloons.Problem(reply.Text));
                return;
            }

            if (!_animatedThisTurn)
            {
                _character.Play("Explain");
            }

            _pager.SetAnswer(reply.Text);
            ShowAnswerPage();
        }
        catch (OperationCanceledException)
        {
            // Cancelled from the balloon, by hiding the assistant or by switching assistants.
        }
        catch (Exception e)
        {
            // For example no credentials at all: the SDK fails before sending anything.
            _character.Play("Alert");
            var hint = _settings.ApiKey is null && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))
                ? "I need a Claude API key before I can help. Click Options and paste one in."
                : e.Message;
            _balloon.Show(ChatBalloons.Problem(hint));
        }
        finally
        {
            if (_approval is { } approval)
            {
                _approval = null;
                approval.TrySetCanceled();
            }

            if (_request?.Token == cancellation)
            {
                _request.Dispose();
                _request = null;
            }
        }
    }

    /// <summary>"See more...": the next page of the answer, keeping the follow-up question typed so far.</summary>
    public void ShowNextPage()
    {
        if (_pager.MoveNext())
        {
            ShowAnswerPage(_balloon.Question);
        }
    }

    public void CompleteApproval(ToolApproval approval)
    {
        var pending = _approval;
        _approval = null;
        ShowStatus();
        pending?.TrySetResult(approval);
    }

    /// <summary>Escape in the balloon: denies a pending tool call, otherwise stops and closes.</summary>
    public void Dismiss()
    {
        if (_approval is not null)
        {
            CompleteApproval(ToolApproval.Deny);
            return;
        }

        CancelRequest();
        _balloon.Close();
    }

    public void CancelRequest()
    {
        if (_request is { } request)
        {
            _request = null;
            request.Cancel();
        }

        if (_approval is { } approval)
        {
            _approval = null;
            approval.TrySetCanceled();
        }
    }

    public void Dispose()
    {
        CancelRequest();
        _ = Mcp.DisposeAsync().AsTask();
    }

    public void ReportStatus(string status, string? animation)
    {
        _status = status;
        ShowStatus();

        // Don't cut short an animation Claude chose (or one that is already running).
        if (animation is not null && !_character.IsPlaying)
        {
            _character.Play(animation);
        }
    }

    public Task<ToolApproval> ApproveAsync(ToolCallRequest request, CancellationToken cancellationToken)
    {
        _approval?.TrySetResult(ToolApproval.Deny);
        var approval = new TaskCompletionSource<ToolApproval>();
        _approval = approval;
        cancellationToken.Register(() => approval.TrySetCanceled(cancellationToken));
        _character.Play("GetAttention");
        _balloon.Show(ChatBalloons.ToolApproval(request.Server, request.Tool, request.Input));
        return approval.Task;
    }

    private void ShowAnswerPage(string followUp = "")
    {
        if (_pager.Current is { } page)
        {
            _balloon.Show(ChatBalloons.Answer(page, hasMore: _pager.HasMore, followUp));
        }
    }

    private AnthropicClient GetClient()
    {
        var key = _settings.ApiKey;
        if (_client is null || key != _clientKey)
        {
            // Without a saved key the SDK finds one itself (ANTHROPIC_API_KEY or an `ant auth login` profile).
            _client = key is null ? new AnthropicClient() : new AnthropicClient { ApiKey = key };
            _clientKey = key;
        }

        return _client;
    }

    private AssistantChat CreateChat()
    {
        var animations = CharacterAnimations.Gestures(_character.Character).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var tools = new List<LocalTool>();
        if (animations.Count > 0)
        {
            tools.Add(new LocalTool(
                SystemPrompt.PlayAnimationTool,
                "Plays one of your character's animations on screen, for example to greet, explain, celebrate or look busy. It runs while you keep talking.",
                AnimationSchema(animations),
                input =>
                {
                    var name = input.TryGetValue("animation", out var value) ? value.GetString() : null;
                    if (name is null || !animations.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException($"Unknown animation. Choose one of: {string.Join(", ", animations)}.");
                    }

                    _animatedThisTurn = _character.Play(name);
                    return _animatedThisTurn ? $"Playing {name}." : "The animation could not be played right now.";
                }));
        }

        var strings = _character.Strings;
        var prompt = SystemPrompt.Build(
            strings?.Name ?? Definition?.Key ?? "the Office Assistant",
            strings?.Description,
            canAnimate: animations.Count > 0,
            Profile?.Instructions,
            DateTime.Now);
        return new AssistantChat(prompt, tools, Mcp);
    }

    private static JsonElement AnimationSchema(IReadOnlyList<string> animations)
    {
        var schemaNode = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["animation"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray([.. animations.Select(a => (JsonNode?)JsonValue.Create(a))]),
                    ["description"] = "The animation to play.",
                },
            },
            ["required"] = new JsonArray("animation"),
        };
        using var document = JsonDocument.Parse(schemaNode.ToJsonString());
        return document.RootElement.Clone();
    }
}
