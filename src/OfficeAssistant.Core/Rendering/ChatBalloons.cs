namespace OfficeAssistant.Core.Rendering;

/// <summary>Balloons for talking to the assistant, in the Office XP style.</summary>
public static class ChatBalloons
{
    public const string AskButton = "ask";
    public const string OptionsButton = BalloonScenarios.OptionsButton;
    public const string NewChatButton = "new";
    public const string CancelButton = "cancel";
    public const string OkButton = "ok";
    public const string AllowButton = "allow";
    public const string AllowAlwaysButton = "allow-always";
    public const string DenyButton = "deny";
    public const string SeeMoreId = BalloonScenarios.SeeMoreId;
    public const string AskPlaceholder = "Type your question here and then click Ask.";

    private const int MaxArgumentLength = 60;
    private const int MaxArguments = 4;

    /// <summary>"What would you like to do?", optionally after a tip or greeting.</summary>
    public static BalloonContent Ask(string? tip = null)
    {
        var blocks = new List<BalloonBlock>();
        if (!string.IsNullOrWhiteSpace(tip))
        {
            blocks.Add(new BalloonText(tip));
        }

        blocks.Add(new BalloonText("What would you like to do?", Bold: true));
        blocks.Add(new BalloonTextBox(AskPlaceholder) { IsPlaceholder = true, SpaceBefore = tip is null ? null : 3 });
        blocks.Add(AskButtons(OptionsButton, "&Options"));
        return new BalloonContent([.. blocks]);
    }

    /// <summary>Shown while Claude works.</summary>
    public static BalloonContent Working(string status) => new(
        new BalloonText(status),
        new BalloonButtons([new BalloonButton("Cancel", CancelButton)]) { SpaceBefore = 10 });

    /// <summary>One page of an answer, with "See more..." when there are more and a box for a follow-up.</summary>
    public static BalloonContent Answer(string page, bool hasMore, string followUp = "")
    {
        var blocks = Paragraphs(page);
        if (hasMore)
        {
            blocks.Add(new BalloonSeeMore("See more...", SeeMoreId));
        }

        blocks.Add(new BalloonTextBox(followUp) { SpaceBefore = 12 });
        blocks.Add(AskButtons(NewChatButton, "&New topic"));
        return new BalloonContent([.. blocks]);
    }

    /// <summary>Something went wrong; offers the options (for example to fix the API key).</summary>
    public static BalloonContent Problem(string message) => new(
        new BalloonIconHeading(BalloonIcon.Warning, "There's a problem"),
        new BalloonText(message),
        new BalloonSeparator(),
        new BalloonButtons([new BalloonButton("&Options", OptionsButton), new BalloonButton("OK", OkButton)]) { DefaultIndex = 1, FocusedIndex = 1 });

    /// <summary>Asks before an MCP tool runs.</summary>
    public static BalloonContent ToolApproval(string server, string tool, IReadOnlyDictionary<string, System.Text.Json.JsonElement> input)
    {
        var blocks = new List<BalloonBlock>
        {
            new BalloonText($"May I use \"{tool}\" from {server}?", Bold: true),
        };

        var arguments = input.Take(MaxArguments).Select(a => $"{a.Key}: {Shorten(a.Value)}").ToList();
        if (input.Count > MaxArguments)
        {
            arguments.Add($"(and {input.Count - MaxArguments} more)");
        }

        if (arguments.Count > 0)
        {
            blocks.Add(new BalloonText(string.Join("\n", arguments)));
        }

        blocks.Add(new BalloonSeparator());
        blocks.Add(new BalloonButtons(
        [
            new BalloonButton("&Allow", AllowButton),
            new BalloonButton("Al&ways", AllowAlwaysButton),
            new BalloonButton("&Deny", DenyButton),
        ])
        {
            DefaultIndex = 0,
            FocusedIndex = 0,
        });
        return new BalloonContent([.. blocks]);
    }

    private static BalloonButtons AskButtons(string otherId, string otherText) =>
        new([new BalloonButton(otherText, otherId), new BalloonButton("&Ask", AskButton)]) { DefaultIndex = 1 };

    private static List<BalloonBlock> Paragraphs(string text)
    {
        var blocks = new List<BalloonBlock>();
        foreach (var paragraph in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            blocks.Add(new BalloonText(paragraph) { SpaceBefore = blocks.Count == 0 ? null : 8 });
        }

        return blocks;
    }

    private static string Shorten(System.Text.Json.JsonElement value)
    {
        var text = value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
        text = text.ReplaceLineEndings(" ");
        return text.Length > MaxArgumentLength ? text[..(MaxArgumentLength - 3)] + "..." : text;
    }
}
