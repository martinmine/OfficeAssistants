using System.Text.Json;
using OfficeAssistant.Core.Chat;
using OfficeAssistant.Core.Rendering;

namespace AcsFormat.Tests;

public class ChatTests
{
    [Fact]
    public void MarkdownBecomesPlainBalloonText()
    {
        var text = AnswerFormatter.ToPlainText("""
            ## Printing

            To print, press **Ctrl+P** and pick a `printer`.

            - First item
            * Second item

            See [the docs](https://example.com) or open my_file_name.txt.
            ```
            code is kept
            ```
            """);

        Assert.Equal(
            "Printing\n\nTo print, press Ctrl+P and pick a printer.\n\n• First item\n• Second item\n\nSee the docs (https://example.com) or open my_file_name.txt.\ncode is kept",
            text);
    }

    [Theory]
    [InlineData("1. First\n2. Second\n   - nested\n   - more\n3. Third", "1. First\n2. Second\n   • nested\n   • more\n3. Third")]
    [InlineData("- outer\n  - inner\n    - innermost", "• outer\n  • inner\n    • innermost")]
    [InlineData("Use `a*b*c` or `__init__` here.", "Use a*b*c or __init__ here.")]
    [InlineData(@"Not \*emphasis\* and 2 \_ 3", "Not *emphasis* and 2 _ 3")]
    [InlineData("See [array[0]](https://example.com/a_(b)).", "See array[0] (https://example.com/a_(b)).")]
    [InlineData("[https://example.com](https://example.com)", "https://example.com")]
    [InlineData("![a diagram](chart.png)", "a diagram")]
    [InlineData("> Quoted:\n> - one\n> - two", "Quoted:\n• one\n• two")]
    [InlineData("Press <kbd>Ctrl</kbd>+<kbd>C</kbd> to copy.", "Press Ctrl+C to copy.")]
    [InlineData("Title\n=====\n\nBody text.", "Title\n\nBody text.")]
    [InlineData("Intro\n\n---\n\n| Key | Action |\n|-----|:------:|\n| F1 | Help |", "Intro\n\nKey  Action\nF1  Help")]
    [InlineData("One\n\n\n\nTwo", "One\n\nTwo")]
    public void MarkdownEdgeCasesBecomePlainText(string markdown, string expected) =>
        Assert.Equal(expected, AnswerFormatter.ToPlainText(markdown));

    [Fact]
    public void ShortAnswersFitOnOnePage()
    {
        var pages = AnswerFormatter.Paginate("Hello!\n\nHow can I help?");
        Assert.Equal(["Hello!\n\nHow can I help?"], pages);
    }

    [Fact]
    public void LongAnswersSplitAtSentencesWithoutLosingText()
    {
        var sentences = Enumerable.Range(1, 60).Select(i => $"This is sentence number {i}.");
        var text = string.Join(" ", sentences);

        var pages = AnswerFormatter.Paginate(text, pageLength: 200);

        Assert.True(pages.Count > 1);
        Assert.All(pages, p => Assert.InRange(p.Length, 1, 200));
        Assert.All(pages, p => Assert.EndsWith(".", p));
        Assert.Equal(text, string.Join(" ", pages));
    }

    [Fact]
    public void ParsesClaudeDesktopMcpConfig()
    {
        var servers = SettingsStore.ParseMcpServers("""
            {
              // Comments and trailing commas are fine.
              "mcpServers": {
                "files": { "command": "npx", "args": ["-y", "server"], "env": { "A": "1" }, "alwaysAllow": ["read_file"] },
                "remote": { "url": "https://example.com/mcp", "headers": { "Authorization": "Bearer x" }, "disabled": true },
              }
            }
            """);

        Assert.Equal(2, servers.Count);
        Assert.Equal("npx", servers["FILES"].Command);
        Assert.Equal(["-y", "server"], servers["files"].Args!);
        Assert.True(servers["files"].IsAlwaysAllowed("READ_FILE"));
        Assert.False(servers["files"].IsAlwaysAllowed("write_file"));
        Assert.True(servers["remote"].Disabled);
        Assert.Equal("Bearer x", servers["remote"].Headers!["Authorization"]);

        var roundTrip = SettingsStore.ParseMcpServers(SettingsStore.FormatMcpServers(servers));
        Assert.Equal(servers.Keys.Order(), roundTrip.Keys.Order());
    }

    [Fact]
    public void RejectsServerWithoutCommandOrUrl()
    {
        Assert.Throws<JsonException>(() => SettingsStore.ParseMcpServers("""{ "broken": { "args": [] } }"""));
    }

    [Fact]
    public void SettingsRoundTripWithEncryptedKey()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oa-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path) { ApiKey = "sk-ant-test" };
            store.Settings.Model = "claude-sonnet-5-5";
            store.Settings.GetProfile("CLIPPIT").Instructions = "Be brief.";
            store.Save();

            Assert.DoesNotContain("sk-ant-test", File.ReadAllText(path));

            var loaded = new SettingsStore(path);
            Assert.Equal("sk-ant-test", loaded.ApiKey);
            Assert.Equal("claude-sonnet-5-5", loaded.Settings.Model);
            Assert.Equal("Be brief.", loaded.Settings.GetProfile("clippit").Instructions);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SavingKeepsUnknownSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oa-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{"PreviewMuted":true}""");
            new SettingsStore(path).Save();
            Assert.Contains("\"PreviewMuted\": true", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ChatBalloonsLayOut()
    {
        using var renderer = new BalloonRenderer();
        var input = new Dictionary<string, JsonElement> { ["path"] = JsonSerializer.SerializeToElement(new string('x', 200)) };
        BalloonContent[] balloons =
        [
            ChatBalloons.Ask(),
            ChatBalloons.Ask("Hi there!"),
            ChatBalloons.Working("Thinking..."),
            ChatBalloons.Answer("First paragraph.\n\nSecond paragraph.", hasMore: true),
            ChatBalloons.Problem("Something broke."),
            ChatBalloons.ToolApproval("files", "read_file", input),
        ];

        foreach (var balloon in balloons)
        {
            var layout = renderer.Measure(balloon, new BalloonState());
            Assert.InRange(layout.Width, 1, 300);
            Assert.True(layout.Height > 0);
        }
    }
}
