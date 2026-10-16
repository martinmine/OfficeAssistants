using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace OfficeAssistant.Core.Chat;

/// <summary>A tool the assistant implements itself (as opposed to one from an MCP server).</summary>
public sealed record LocalTool(string Name, string Description, JsonElement InputSchema, Func<IReadOnlyDictionary<string, JsonElement>, string> Run);

/// <summary>A tool call Claude wants to make, shown to the user for approval.</summary>
public sealed record ToolCallRequest(string Server, string Tool, IReadOnlyDictionary<string, JsonElement> Input);

public enum ToolApproval
{
    Deny,
    Allow,
    AllowAlways,
}

/// <summary>What the chat needs from the on-screen assistant while it works.</summary>
public interface IChatHost
{
    /// <summary>Shows progress, such as "Thinking..." or the tool being used.</summary>
    void ReportStatus(string status, string? animation);

    /// <summary>Asks the user whether an MCP tool may run.</summary>
    Task<ToolApproval> ApproveAsync(ToolCallRequest request, CancellationToken cancellationToken);
}

/// <summary>The outcome of one question.</summary>
public sealed record ChatReply(string Text, bool IsError = false);

/// <summary>
/// One conversation with Claude, with the assistant's own tools and the tools of its MCP
/// servers. Run it on the UI thread: tool calls and host callbacks happen there.
/// </summary>
public sealed class AssistantChat
{
    private const int MaxTokens = 16000;
    private const int MaxToolRounds = 25;
    private const int MaxToolResultLength = 100_000;
    private const string FallbackBeta = "server-side-fallback-2026-07-01";

    private readonly List<BetaMessageParam> _history = [];
    private readonly HashSet<string> _alwaysAllowed = new(StringComparer.Ordinal);

    public AssistantChat(string systemPrompt, IReadOnlyList<LocalTool> localTools, McpHub mcp)
    {
        SystemPrompt = systemPrompt;
        LocalTools = localTools;
        Mcp = mcp;
    }

    public string SystemPrompt { get; }

    public IReadOnlyList<LocalTool> LocalTools { get; }

    public McpHub Mcp { get; }

    public bool IsBusy { get; private set; }

    public bool HasHistory => _history.Count > 0;

    public void Reset()
    {
        if (!IsBusy)
        {
            _history.Clear();
            _alwaysAllowed.Clear();
        }
    }

    public async Task<ChatReply> AskAsync(AnthropicClient client, string model, string question, IChatHost host, CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("The assistant is already answering a question.");
        }

        IsBusy = true;
        var historyLength = _history.Count;
        try
        {
            _history.Add(new BetaMessageParam { Role = Role.User, Content = question });
            var reply = await RunAsync(client, model, host, cancellationToken);
            if (reply.IsError)
            {
                RollBack(historyLength);
            }

            return reply;
        }
        catch (OperationCanceledException)
        {
            RollBack(historyLength);
            throw;
        }
        catch (AnthropicApiException e)
        {
            RollBack(historyLength);
            return new ChatReply(DescribeApiError(e), IsError: true);
        }
        catch (Exception e) when (e is HttpRequestException or AnthropicException)
        {
            RollBack(historyLength);
            return new ChatReply($"I couldn't reach Claude: {e.Message}", IsError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<ChatReply> RunAsync(AnthropicClient client, string model, IChatHost host, CancellationToken cancellationToken)
    {
        // Snapshot the tools for the whole turn so the list (and the prompt cache) stays stable.
        var mcpTools = Mcp.Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var local = LocalTools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        List<BetaToolUnion> tools =
        [
            .. LocalTools.Select(t => (BetaToolUnion)CreateTool(t.Name, t.Description, t.InputSchema)),
            .. mcpTools.Values.Select(t => (BetaToolUnion)CreateTool(t.Name, $"[{t.Server}] {t.Description}", t.InputSchema)),
        ];

        var text = new StringBuilder();
        for (var round = 0; round < MaxToolRounds; round++)
        {
            host.ReportStatus(round == 0 ? "Thinking..." : "Thinking about what I found...", round == 0 ? "Thinking" : "Searching");
            var parameters = new MessageCreateParams
            {
                Model = model,
                MaxTokens = MaxTokens,
                System = SystemPrompt,
                Messages = [.. _history],
                Tools = tools,
                CacheControl = new BetaCacheControlEphemeral(),
                Betas = [FallbackBeta],
                Fallbacks = new Default(),
            };

            var response = await client.Beta.Messages.Create(parameters, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (response.StopReason == "refusal")
            {
                return new ChatReply("I'm sorry, but I can't help with that one.", IsError: true);
            }

            var assistantContent = new List<BetaContentBlockParam>();
            var toolUses = new List<BetaToolUseBlock>();
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out var t))
                {
                    assistantContent.Add(new BetaTextBlockParam { Text = t.Text });
                    text.Append(t.Text);
                }
                else if (block.TryPickThinking(out var thinking))
                {
                    assistantContent.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                }
                else if (block.TryPickRedactedThinking(out var redacted))
                {
                    assistantContent.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
                }
                else if (block.TryPickToolUse(out var toolUse))
                {
                    assistantContent.Add(new BetaToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
                    toolUses.Add(toolUse);
                }
                else if (block.TryPickFallback(out var fallback))
                {
                    assistantContent.Add(BetaFallbackBlockParam.FromRawUnchecked(fallback.RawData));
                }
            }

            if (assistantContent.Count > 0)
            {
                _history.Add(new BetaMessageParam { Role = Role.Assistant, Content = assistantContent });
            }

            if (response.StopReason != "tool_use" || toolUses.Count == 0)
            {
                var answer = text.ToString().Trim();
                if (response.StopReason == "max_tokens")
                {
                    answer += answer.Length > 0 ? "\n\n(I ran out of room for the rest.)" : "I ran out of room before I could answer.";
                }

                return new ChatReply(answer.Length > 0 ? answer : "I'm not sure what to say to that.");
            }

            // Text before tool calls is a lead-in ("Let me check..."); keep a gap before what follows.
            if (text.Length > 0)
            {
                text.Append("\n\n");
            }

            var results = new List<BetaContentBlockParam>();
            foreach (var toolUse in toolUses)
            {
                var (content, isError) = await RunToolAsync(toolUse, local, mcpTools, host, cancellationToken);
                results.Add(new BetaToolResultBlockParam { ToolUseID = toolUse.ID, Content = content, IsError = isError });
            }

            _history.Add(new BetaMessageParam { Role = Role.User, Content = results });
        }

        return new ChatReply("That took more steps than I can manage. Try asking for something smaller.", IsError: true);
    }

    private async Task<(string Content, bool IsError)> RunToolAsync(
        BetaToolUseBlock toolUse,
        Dictionary<string, LocalTool> local,
        Dictionary<string, McpTool> mcp,
        IChatHost host,
        CancellationToken cancellationToken)
    {
        if (local.TryGetValue(toolUse.Name, out var localTool))
        {
            try
            {
                return (localTool.Run(toolUse.Input), false);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                return (e.Message, true);
            }
        }

        if (!mcp.TryGetValue(toolUse.Name, out var tool))
        {
            return ($"Unknown tool: {toolUse.Name}", true);
        }

        var toolName = tool.Tool.Name;
        if (!tool.Config.IsAlwaysAllowed(toolName) && !_alwaysAllowed.Contains(tool.Name))
        {
            var approval = await host.ApproveAsync(new ToolCallRequest(tool.Server, toolName, toolUse.Input), cancellationToken);
            if (approval == ToolApproval.Deny)
            {
                return ("The user declined to run this tool.", true);
            }

            if (approval == ToolApproval.AllowAlways)
            {
                _alwaysAllowed.Add(tool.Name);
            }
        }

        host.ReportStatus($"Using {tool.Server}: {toolName}...", "GetTechy");
        try
        {
            var arguments = toolUse.Input.ToDictionary(p => p.Key, p => (object?)p.Value);
            var result = await tool.Tool.CallAsync(arguments, cancellationToken: cancellationToken);
            var content = McpHub.FormatResult(result);
            if (content.Length > MaxToolResultLength)
            {
                content = content[..MaxToolResultLength] + "\n[output truncated]";
            }

            return (content, result.IsError == true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            return ($"The tool failed: {e.GetBaseException().Message}", true);
        }
    }

    private static BetaTool CreateTool(string name, string description, JsonElement schema)
    {
        var raw = schema.ValueKind == JsonValueKind.Object
            ? schema.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
            : [];
        raw["type"] = JsonSerializer.SerializeToElement("object", SettingsJsonContext.Default.String);
        return new BetaTool
        {
            Name = name,
            Description = description,
            InputSchema = InputSchema.FromRawUnchecked(raw),
        };
    }

    private void RollBack(int length)
    {
        // Only ever drop the unanswered tail: earlier turns stay exactly as sent.
        if (_history.Count > length)
        {
            _history.RemoveRange(length, _history.Count - length);
        }
    }

    private static string DescribeApiError(AnthropicApiException e) => e switch
    {
        AnthropicUnauthorizedException => "Claude didn't accept the API key. Check it under Options.",
        AnthropicRateLimitException => "Claude is busy right now (rate limited). Try again in a moment.",
        Anthropic5xxException => "Claude is having trouble right now. Try again in a moment.",
        _ => $"Claude returned an error: {e.Message}",
    };
}
