using AcsFormat;
using OfficeAssistant.Core.Chat;
using OfficeAssistant.Core.Rendering;
using OfficeAssistant.Core.Windows;

namespace OfficeAssistant.Core;

/// <summary>
/// Runs the on-screen assistant: the character window, its balloon, animations,
/// sounds, idling and the conversation with Claude (including the tools of the
/// assistant's MCP servers). Create and use it on the UI thread.
/// </summary>
/// <remarks>
/// This is the facade the app uses. The character is run by <see cref="CharacterPresenter"/>,
/// the balloon placement by <see cref="BalloonCoordinator"/> and the conversation by
/// <see cref="ConversationController"/>; this class wires their events together.
/// </remarks>
public sealed class AssistantController : IDisposable, IChatHost
{
    private const int MenuHide = 1;
    private const int MenuOptions = 2;
    private const int MenuChoose = 3;
    private const int MenuAnimate = 4;
    private const int MenuNewTopic = 5;

    private readonly CharacterPresenter _character;
    private readonly BalloonCoordinator _balloon;
    private readonly ConversationController _conversation;

    /// <param name="settings">The Claude settings and the assistants' profiles.</param>
    /// <param name="textInput">The text box the app shows over the balloon while the user types; the controller disposes it.</param>
    public AssistantController(SettingsStore settings, IBalloonTextInput textInput)
    {
        _character = new CharacterPresenter(settings.Settings.PixelSize);
        _balloon = new BalloonCoordinator(_character, textInput);
        _conversation = new ConversationController(settings, _character, _balloon);

        _character.VisibilityChanged += (_, _) => VisibilityChanged?.Invoke(this, EventArgs.Empty);
        _character.AnimationStarted += (_, name) => AnimationStarted?.Invoke(this, name);
        _character.Window.Clicked += (_, _) => OnCharacterClicked();
        _character.Window.DoubleClicked += (_, _) => Animate();
        _character.Window.ContextMenuRequested += (_, point) => ShowContextMenu(point.X, point.Y);
        _balloon.Window.ButtonClicked += (_, id) => OnBalloonButton(id);
        _balloon.Window.LabelClicked += (_, id) => OnBalloonLabel(id);
        _balloon.Window.Dismissed += (_, _) => _conversation.Dismiss();
    }

    public event EventHandler? VisibilityChanged;

    public event EventHandler? ChooseAssistantRequested;

    public event EventHandler? OptionsRequested;

    /// <summary>Raised with the animation name whenever one starts.</summary>
    public event EventHandler<string>? AnimationStarted;

    public AssistantDefinition? Definition { get; private set; }

    public AcsCharacter? Character => _character.Character;

    /// <summary>The MCP servers of the active assistant.</summary>
    public McpHub Mcp => _conversation.Mcp;

    /// <summary>The active assistant's instructions and MCP servers.</summary>
    public AssistantProfile? Profile => _conversation.Profile;

    public bool IsShown => _character.IsShown;

    /// <summary>True while the character is on screen, including while its hide animation plays.</summary>
    public bool IsOnScreen => _character.IsOnScreen;

    public bool IsBusy => _conversation.IsBusy;

    /// <summary>Windows language id used for the character's localized strings.</summary>
    public int LanguageId
    {
        get => _character.LanguageId;
        set => _character.LanguageId = value;
    }

    public AcsLocalizedInfo? Strings => _character.Strings;

    public bool SoundsEnabled
    {
        get => _character.SoundsEnabled;
        set => _character.SoundsEnabled = value;
    }

    /// <summary>
    /// Integer pixel enlargement in use (1 = identical to the original). Setting it is an
    /// explicit choice, the same as setting <see cref="PreferredScale"/>.
    /// </summary>
    public int Scale
    {
        get => _character.Scale;
        set => PreferredScale = value;
    }

    /// <summary>
    /// The pixel size the user picked, or null to follow the display: round(DPI / 96), so
    /// 1x at 100% scaling and 2x at 200%. Integer steps keep the art sharp.
    /// </summary>
    public int? PreferredScale
    {
        get => _character.PreferredScale;
        set => _character.PreferredScale = value;
    }

    /// <summary>Default pixel size for a monitor DPI: round(DPI / 96), from 1x to 4x.</summary>
    public static int ScaleForDpi(uint dpi) => CharacterPresenter.ScaleForDpi(dpi);

    /// <summary>Switches to another assistant, keeping it on screen if one was shown.</summary>
    public void Load(AssistantDefinition definition, bool greet = true)
    {
        var wasShown = IsShown;
        UnloadCharacter();

        Definition = definition;
        _conversation.Definition = definition;
        _character.Load(definition.LoadCharacter());
        _conversation.ResetChat();
        _ = ConnectMcpServersAsync();

        if (wasShown)
        {
            Show(greet);
        }
    }

    /// <summary>Reconnects to the active assistant's MCP servers after they were edited.</summary>
    public Task ConnectMcpServersAsync() => _conversation.ConnectMcpServersAsync();

    /// <summary>Forgets the conversation so the next question starts fresh (and picks up new instructions).</summary>
    public void NewConversation() => _conversation.NewConversation();

    public void Show(bool greet = true) =>
        _character.Show(() =>
        {
            if (greet && _character.PickGreeting() is { } greeting)
            {
                ShowBalloon(ChatBalloons.Ask(greeting), activate: false);
            }
        });

    public void Hide()
    {
        if (!_character.IsOnScreen || Character is null)
        {
            return;
        }

        _conversation.CancelRequest();
        CloseBalloon();
        _character.Hide();
    }

    /// <summary>Plays a named animation on the visible assistant.</summary>
    public bool Play(string animation) => _character.Play(animation);

    /// <summary>The context menu's "Animate!": a random gesture.</summary>
    public void Animate() => _character.Animate();

    /// <summary>Opens "What would you like to do?".</summary>
    public void ShowAskBalloon(string? tip = null) => ShowBalloon(ChatBalloons.Ask(tip));

    public void ShowBalloon(BalloonContent content, bool activate = true) => _balloon.Show(content, activate);

    public void CloseBalloon() => _balloon.Close();

    public void Dispose()
    {
        _conversation.CancelRequest();
        UnloadCharacter();
        _conversation.Dispose();
        _balloon.Dispose();
        _character.Dispose();
    }

    void IChatHost.ReportStatus(string status, string? animation) => _conversation.ReportStatus(status, animation);

    Task<ToolApproval> IChatHost.ApproveAsync(ToolCallRequest request, CancellationToken cancellationToken) =>
        _conversation.ApproveAsync(request, cancellationToken);

    private void UnloadCharacter()
    {
        _conversation.CancelRequest();
        _balloon.Close();
        _character.Unload();
    }

    private void OnCharacterClicked()
    {
        _character.MarkActive();
        if (_conversation.IsAwaitingApproval)
        {
            return;
        }

        if (IsBusy)
        {
            _conversation.ShowStatus();
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
        var choice = _character.Window.ShowMenu(x, y,
        [
            (MenuHide, "&Hide", true),
            (MenuOptions, "&Options...", true),
            (MenuChoose, "&Choose Assistant...", true),
            (MenuNewTopic, "&New Topic", _conversation.HasHistory || IsBusy),
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
        _character.MarkActive();
        switch (id)
        {
            case ChatBalloons.AskButton:
                _conversation.Ask(_balloon.Question);
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
                _conversation.CancelRequest();
                CloseBalloon();
                break;
            case ChatBalloons.AllowButton:
                _conversation.CompleteApproval(ToolApproval.Allow);
                break;
            case ChatBalloons.AllowAlwaysButton:
                _conversation.CompleteApproval(ToolApproval.AllowAlways);
                break;
            case ChatBalloons.DenyButton:
                _conversation.CompleteApproval(ToolApproval.Deny);
                break;
            default:
                CloseBalloon();
                break;
        }
    }

    private void OnBalloonLabel(string id)
    {
        _character.MarkActive();
        if (id == ChatBalloons.SeeMoreId)
        {
            _conversation.ShowNextPage();
        }
    }
}
