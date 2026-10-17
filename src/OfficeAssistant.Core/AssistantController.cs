using System.Text.Json;
using System.Text.Json.Nodes;
using AcsFormat;
using Anthropic;
using OfficeAssistant.Core.Animation;
using OfficeAssistant.Core.Chat;
using OfficeAssistant.Core.Rendering;
using OfficeAssistant.Core.Windows;

namespace OfficeAssistant.Core;

/// <summary>
/// Runs the on-screen assistant: the character window, its balloon, animations,
/// sounds, idling and the conversation with Claude (including the tools of the
/// assistant's MCP servers). Create and use it on the UI thread.
/// </summary>
public sealed class AssistantController : IDisposable, IChatHost
{
    private const int MenuHide = 1;
    private const int MenuOptions = 2;
    private const int MenuChoose = 3;
    private const int MenuAnimate = 4;
    private const int MenuNewTopic = 5;
    private const string PlayAnimationTool = "play_animation";

    private static readonly string[] NonGestureAnimations = ["Show", "Hide", "RestPose", "Hearing_1", "Alert", "GoodBye"];

    private readonly SettingsStore _settings;
    private readonly CharacterWindow _window = new();
    private readonly BalloonWindow _balloon = new();
    private readonly UiTimer _idleTimer = new();
    private readonly Random _random = new();

    private AnimationPlayer? _player;
    private SoundPlayer? _sounds;
    private uint[]? _lastFrame;
    private bool _positioned;
    private bool _hiding;
    private DateTime _idleSince = DateTime.UtcNow;
    private int _scale = 1;
    private bool _soundsEnabled = true;

    private AssistantChat? _chat;
    private AnthropicClient? _client;
    private string? _clientKey;
    private CancellationTokenSource? _request;
    private TaskCompletionSource<ToolApproval>? _approval;
    private string _status = string.Empty;
    private bool _animatedThisTurn;
    private IReadOnlyList<string> _pages = [];
    private int _page;

    public AssistantController(SettingsStore settings)
    {
        _settings = settings;
        _window.Clicked += (_, _) => OnCharacterClicked();
        _window.DoubleClicked += (_, _) => Animate();
        _window.ContextMenuRequested += (_, point) => ShowContextMenu(point.X, point.Y);
        _window.Moved += (_, _) => _balloon.UpdateAnchor(FrameBounds, _scale);
        _balloon.ButtonClicked += (_, id) => OnBalloonButton(id);
        _balloon.LabelClicked += (_, id) => OnBalloonLabel(id);
        _balloon.Dismissed += (_, _) => OnBalloonDismissed();
        _settings.Saved += (_, _) => _client = null;
    }

    public event EventHandler? VisibilityChanged;

    public event EventHandler? ChooseAssistantRequested;

    public event EventHandler? OptionsRequested;

    /// <summary>Raised with the animation name whenever one starts.</summary>
    public event EventHandler<string>? AnimationStarted;

    public AssistantDefinition? Definition { get; private set; }

    public AcsCharacter? Character { get; private set; }

    /// <summary>The MCP servers of the active assistant.</summary>
    public McpHub Mcp { get; } = new();

    /// <summary>The active assistant's instructions and MCP servers.</summary>
    public AssistantProfile? Profile => Definition is null ? null : _settings.Settings.GetProfile(Definition.Key);

    public bool IsShown => _window.IsVisible && !_hiding;

    /// <summary>True while the character is on screen, including while its hide animation plays.</summary>
    public bool IsOnScreen => _window.IsVisible;

    public bool IsBusy => _chat?.IsBusy == true;

    /// <summary>Windows language id used for the character's localized strings.</summary>
    public int LanguageId { get; set; } = System.Globalization.CultureInfo.CurrentUICulture.LCID;

    public AcsLocalizedInfo? Strings => Character?.LocalizedInfo.Count > 0 ? Character.GetLocalizedInfo(LanguageId) : null;

    public bool SoundsEnabled
    {
        get => _soundsEnabled;
        set
        {
            _soundsEnabled = value;
            if (_sounds is not null)
            {
                _sounds.Enabled = value;
            }
        }
    }

    /// <summary>Integer pixel enlargement (1 = identical to the original).</summary>
    public int Scale
    {
        get => _scale;
        set
        {
            value = Math.Clamp(value, 1, 4);
            if (value == _scale)
            {
                return;
            }

            // Keep the character's feet where they are.
            var oldBottom = _window.Y + _window.Height;
            _scale = value;
            if (_lastFrame is not null && Character is not null)
            {
                Present(_lastFrame, _window.X, oldBottom - (Character.Height * value));
                _balloon.UpdateAnchor(FrameBounds, _scale);
            }
        }
    }

    private (int X, int Y, int Width, int Height) FrameBounds =>
        (_window.X, _window.Y, (Character?.Width ?? 0) * _scale, (Character?.Height ?? 0) * _scale);

    /// <summary>Switches to another assistant, keeping it on screen if one was shown.</summary>
    public void Load(AssistantDefinition definition, bool greet = true)
    {
        var wasShown = IsShown;
        UnloadCharacter();

        Definition = definition;
        Character = definition.LoadCharacter();
        _sounds = new SoundPlayer(Character) { Enabled = SoundsEnabled };
        _player = new AnimationPlayer(Character, frame => Present(frame, _window.X, _window.Y), _sounds.Play);
        _chat = null;
        _ = ConnectMcpServersAsync();

        if (wasShown)
        {
            Show(greet);
        }
    }

    /// <summary>Reconnects to the active assistant's MCP servers after they were edited.</summary>
    public Task ConnectMcpServersAsync() =>
        Mcp.ConnectAsync(Profile?.McpServers ?? new Dictionary<string, McpServerConfig>());

    /// <summary>Forgets the conversation so the next question starts fresh (and picks up new instructions).</summary>
    public void NewConversation()
    {
        CancelRequest();
        _chat = null;
        _pages = [];
    }

    public void Show(bool greet = true)
    {
        if (Character is null || _player is null)
        {
            return;
        }

        if (!_positioned)
        {
            var work = LayeredWindow.GetWorkArea(0, 0);
            var x = work.Right - (Character.Width * _scale) - 40;
            var y = work.Bottom - (Character.Height * _scale) - 40;
            _window.MoveTo(x, y);
            _positioned = true;
        }

        _hiding = false;
        var showAnimation = Character.GetStateAnimations("SHOWING").FirstOrDefault() ?? "Show";
        _player.Abort();
        if (Character.FindAnimation(showAnimation) is not null)
        {
            _player.ShowStill(showAnimation);
        }
        else
        {
            ShowRestPose();
        }

        _window.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
        PlayInternal(showAnimation, () =>
        {
            if (greet && Strings?.Greetings is { Count: > 0 } greetings)
            {
                ShowBalloon(ChatBalloons.Ask(greetings[_random.Next(greetings.Count)]), activate: false);
            }
        });
    }

    public void Hide()
    {
        if (!_window.IsVisible || Character is null)
        {
            return;
        }

        CancelRequest();
        CloseBalloon();
        _idleTimer.Stop();
        _hiding = true;
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
        var hideAnimation = Character.GetStateAnimations("HIDING").FirstOrDefault() ?? "Hide";
        if (!PlayInternal(hideAnimation, HideNow))
        {
            HideNow();
        }
    }

    /// <summary>Plays a named animation on the visible assistant.</summary>
    public bool Play(string animation)
    {
        if (!IsShown)
        {
            return false;
        }

        _idleSince = DateTime.UtcNow;
        return PlayInternal(animation, null);
    }

    /// <summary>The context menu's "Animate!": a random gesture.</summary>
    public void Animate()
    {
        if (Character is null)
        {
            return;
        }

        var candidates = Gestures().ToList();
        if (candidates.Count == 0)
        {
            candidates = [.. Character.Animations.Select(a => a.Name)];
        }

        Play(candidates[_random.Next(candidates.Count)]);
    }

    /// <summary>Opens "What would you like to do?".</summary>
    public void ShowAskBalloon(string? tip = null) => ShowBalloon(ChatBalloons.Ask(tip));

    public void ShowBalloon(BalloonContent content, bool activate = true)
    {
        if (!IsShown)
        {
            return;
        }

        _balloon.ShowContent(content, FrameBounds, _scale, activate);
    }

    public void CloseBalloon() => _balloon.Close();

    public void Dispose()
    {
        CancelRequest();
        UnloadCharacter();
        _ = Mcp.DisposeAsync().AsTask();
        _idleTimer.Dispose();
        _balloon.Dispose();
        _window.Dispose();
    }

    void IChatHost.ReportStatus(string status, string? animation)
    {
        _status = status;
        ShowBalloon(ChatBalloons.Working(status), activate: false);

        // Don't cut short an animation Claude chose (or one that is already running).
        if (animation is not null && _player?.IsPlaying != true)
        {
            Play(animation);
        }
    }

    Task<ToolApproval> IChatHost.ApproveAsync(ToolCallRequest request, CancellationToken cancellationToken)
    {
        _approval?.TrySetResult(ToolApproval.Deny);
        var approval = new TaskCompletionSource<ToolApproval>();
        _approval = approval;
        cancellationToken.Register(() => approval.TrySetCanceled(cancellationToken));
        Play("GetAttention");
        ShowBalloon(ChatBalloons.ToolApproval(request.Server, request.Tool, request.Input));
        return approval.Task;
    }

    private void UnloadCharacter()
    {
        CancelRequest();
        _idleTimer.Stop();
        _balloon.Close();
        _player?.Abort();
        _player?.Dispose();
        _sounds?.Dispose();
        _player = null;
        _sounds = null;
        _lastFrame = null;
        _window.Hide();
        Character = null;
    }

    private void HideNow()
    {
        _window.Hide();
        _hiding = false;
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool PlayInternal(string animation, Action? completed)
    {
        if (_player is null)
        {
            return false;
        }

        _idleTimer.Stop();
        if (_player.IsPlaying)
        {
            // Cut the running animation short, including its sound.
            _sounds?.Stop();
        }

        var started = _player.Play(animation, () =>
        {
            completed?.Invoke();
            ScheduleIdle();
        });
        if (started)
        {
            AnimationStarted?.Invoke(this, Character?.FindAnimation(animation)?.Name ?? animation);
        }

        return started;
    }

    private void ShowRestPose()
    {
        if (Character is null || _player is null)
        {
            return;
        }

        var rest = Character.FindAnimation("RestPose") ?? Character.Animations.FirstOrDefault();
        if (rest is not null)
        {
            _player.ShowStill(rest.Name);
        }
    }

    private void ScheduleIdle()
    {
        if (Character is null || _hiding || !_window.IsVisible || _player?.IsPlaying == true)
        {
            return;
        }

        // Gallery-only assistants have nothing but their wave, so idle less often.
        var (min, max) = Character.Animations.Count > 3 ? (6_000, 12_000) : (25_000, 45_000);
        _idleTimer.Start(_random.Next(min, max), PlayIdle);
    }

    private void PlayIdle()
    {
        if (Character is null || !IsShown)
        {
            return;
        }

        var idleSeconds = (DateTime.UtcNow - _idleSince).TotalSeconds;
        var level = idleSeconds < 60 ? 1 : idleSeconds < 180 ? 2 : 3;
        IReadOnlyList<string> animations = [];
        for (; level >= 1 && animations.Count == 0; level--)
        {
            animations = Character.GetStateAnimations($"IDLINGLEVEL{level}");
        }

        if (animations.Count == 0)
        {
            return;
        }

        PlayInternal(animations[_random.Next(animations.Count)], null);
    }

    private void Present(uint[] frame, int x, int y)
    {
        if (Character is null)
        {
            return;
        }

        _lastFrame = frame;
        _window.Present(frame, Character.Width, Character.Height, x, y, _scale);
    }

    private IEnumerable<string> Gestures() =>
        Character?.Animations
            .Select(a => a.Name)
            .Where(name => !name.StartsWith("Idle", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("Look", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("Gesture", StringComparison.OrdinalIgnoreCase)
                && !NonGestureAnimations.Contains(name, StringComparer.OrdinalIgnoreCase))
        ?? [];

    private void OnCharacterClicked()
    {
        _idleSince = DateTime.UtcNow;
        if (_approval is not null)
        {
            return;
        }

        if (IsBusy)
        {
            ShowBalloon(ChatBalloons.Working(_status), activate: false);
            return;
        }

        if (_balloon.IsVisible)
        {
            CloseBalloon();
            return;
        }

        ShowAskBalloon();
    }

    private void ShowContextMenu(int x, int y)
    {
        var choice = _window.ShowMenu(x, y,
        [
            (MenuHide, "&Hide", true),
            (MenuOptions, "&Options...", true),
            (MenuChoose, "&Choose Assistant...", true),
            (MenuNewTopic, "&New Topic", _chat?.HasHistory == true || IsBusy),
            (MenuAnimate, "&Animate!", true),
        ]);

        switch (choice)
        {
            case MenuHide:
                Hide();
                break;
            case MenuOptions:
                OptionsRequested?.Invoke(this, EventArgs.Empty);
                break;
            case MenuChoose:
                ChooseAssistantRequested?.Invoke(this, EventArgs.Empty);
                break;
            case MenuNewTopic:
                NewConversation();
                ShowAskBalloon();
                break;
            case MenuAnimate:
                Animate();
                break;
        }
    }

    private void OnBalloonButton(string id)
    {
        _idleSince = DateTime.UtcNow;
        switch (id)
        {
            case ChatBalloons.AskButton:
                Ask(_balloon.Question);
                break;
            case ChatBalloons.OptionsButton:
                CloseBalloon();
                OptionsRequested?.Invoke(this, EventArgs.Empty);
                break;
            case ChatBalloons.NewChatButton:
                NewConversation();
                ShowAskBalloon();
                break;
            case ChatBalloons.CancelButton:
                CancelRequest();
                CloseBalloon();
                break;
            case ChatBalloons.AllowButton:
                CompleteApproval(ToolApproval.Allow);
                break;
            case ChatBalloons.AllowAlwaysButton:
                CompleteApproval(ToolApproval.AllowAlways);
                break;
            case ChatBalloons.DenyButton:
                CompleteApproval(ToolApproval.Deny);
                break;
            default:
                CloseBalloon();
                break;
        }
    }

    private void OnBalloonLabel(string id)
    {
        _idleSince = DateTime.UtcNow;
        if (id == ChatBalloons.SeeMoreId && _pages.Count > 1)
        {
            _page = (_page + 1) % _pages.Count;
            ShowAnswerPage(_balloon.Question);
        }
    }

    private void OnBalloonDismissed()
    {
        if (_approval is not null)
        {
            CompleteApproval(ToolApproval.Deny);
            return;
        }

        CancelRequest();
        CloseBalloon();
    }

    private void CompleteApproval(ToolApproval approval)
    {
        var pending = _approval;
        _approval = null;
        ShowBalloon(ChatBalloons.Working(_status), activate: false);
        pending?.TrySetResult(approval);
    }

    private async void Ask(string question)
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
                Play("Alert");
                ShowBalloon(ChatBalloons.Problem(reply.Text));
                return;
            }

            if (!_animatedThisTurn)
            {
                Play("Explain");
            }

            _pages = AnswerFormatter.Paginate(AnswerFormatter.ToPlainText(reply.Text));
            _page = 0;
            ShowAnswerPage();
        }
        catch (OperationCanceledException)
        {
            // Cancelled from the balloon, by hiding the assistant or by switching assistants.
        }
        catch (Exception e)
        {
            // For example no credentials at all: the SDK fails before sending anything.
            Play("Alert");
            var hint = _settings.ApiKey is null && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))
                ? "I need a Claude API key before I can help. Click Options and paste one in."
                : e.Message;
            ShowBalloon(ChatBalloons.Problem(hint));
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

    private void ShowAnswerPage(string followUp = "")
    {
        if (_pages.Count == 0)
        {
            return;
        }

        ShowBalloon(ChatBalloons.Answer(_pages[_page], hasMore: _pages.Count > 1, followUp));
    }

    private void CancelRequest()
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
        var animations = Gestures().Order(StringComparer.OrdinalIgnoreCase).ToList();
        var tools = new List<LocalTool>();
        if (animations.Count > 0)
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
            var schema = document.RootElement.Clone();
            tools.Add(new LocalTool(
                PlayAnimationTool,
                "Plays one of your character's animations on screen, for example to greet, explain, celebrate or look busy. It runs while you keep talking.",
                schema,
                input =>
                {
                    var name = input.TryGetValue("animation", out var value) ? value.GetString() : null;
                    if (name is null || !animations.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException($"Unknown animation. Choose one of: {string.Join(", ", animations)}.");
                    }

                    _animatedThisTurn = Play(name);
                    return _animatedThisTurn ? $"Playing {name}." : "The animation could not be played right now.";
                }));
        }

        return new AssistantChat(BuildSystemPrompt(), tools, Mcp);
    }

    private string BuildSystemPrompt()
    {
        var strings = Strings;
        var name = strings?.Name ?? Definition?.Key ?? "the Office Assistant";
        var prompt = new System.Text.StringBuilder();
        prompt.AppendLine($"You are {name}, one of the Office Assistant characters from Microsoft Office, now living on the user's Windows desktop as a helpful companion.");
        if (!string.IsNullOrWhiteSpace(strings?.Description))
        {
            prompt.AppendLine($"Your character description: {strings.Description}");
        }

        prompt.AppendLine();
        prompt.AppendLine("You talk to the user through a small yellow speech balloon next to your character, about 300 pixels wide.");
        prompt.AppendLine("- Keep answers short, warm and to the point: usually one to three short paragraphs. Longer answers are split into pages the user has to click through.");
        prompt.AppendLine("- Write plain text. The balloon can't show Markdown, so leave out headings, bold, tables and code blocks. Simple lists starting with \"- \" are fine.");
        prompt.AppendLine("- Stay in character, with the cheerful helpfulness the Office Assistants were known for, but put being useful first.");
        if (Gestures().Any())
        {
            prompt.AppendLine($"- Use the {PlayAnimationTool} tool to react with your character when it fits, such as greeting, thinking, explaining or celebrating. One animation per answer is plenty.");
        }

        prompt.AppendLine("- Tools whose names start with a server name come from the user's MCP servers. Use them when they help. The user approves each call; if they decline, carry on without it and don't ask again for the same thing.");
        prompt.AppendLine($"Today's date is {DateTime.Now:dddd d MMMM yyyy}.");

        if (!string.IsNullOrWhiteSpace(Profile?.Instructions))
        {
            prompt.AppendLine();
            prompt.AppendLine("Additional instructions from the user:");
            prompt.AppendLine(Profile.Instructions.Trim());
        }

        return prompt.ToString();
    }
}
