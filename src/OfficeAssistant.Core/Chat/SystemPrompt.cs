using System.Text;

namespace OfficeAssistant.Core.Chat;

/// <summary>Writes the system prompt that puts Claude in character.</summary>
internal static class SystemPrompt
{
    /// <summary>Name of the local tool that plays one of the character's animations.</summary>
    public const string PlayAnimationTool = "play_animation";

    /// <param name="name">The character's name, such as "Clippit".</param>
    /// <param name="description">The character's own description, if it has one.</param>
    /// <param name="canAnimate">Whether the <see cref="PlayAnimationTool"/> tool is offered.</param>
    /// <param name="instructions">Extra instructions from the user's profile.</param>
    /// <param name="today">The date to tell Claude.</param>
    public static string Build(string name, string? description, bool canAnimate, string? instructions, DateTime today)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine($"You are {name}, one of the Office Assistant characters from Microsoft Office, now living on the user's Windows desktop as a helpful companion.");
        if (!string.IsNullOrWhiteSpace(description))
        {
            prompt.AppendLine($"Your character description: {description}");
        }

        prompt.AppendLine();
        prompt.AppendLine("You talk to the user through a small yellow speech balloon next to your character, about 300 pixels wide.");
        prompt.AppendLine("- Keep answers short, warm and to the point: usually one to three short paragraphs. Longer answers are split into pages the user has to click through.");
        prompt.AppendLine("- Write plain text. The balloon can't show Markdown, so leave out headings, bold, tables and code blocks. Simple lists starting with \"- \" are fine.");
        prompt.AppendLine("- Stay in character, with the cheerful helpfulness the Office Assistants were known for, but put being useful first.");
        if (canAnimate)
        {
            prompt.AppendLine($"- Use the {PlayAnimationTool} tool to react with your character when it fits, such as greeting, thinking, explaining or celebrating. One animation per answer is plenty.");
        }

        prompt.AppendLine("- Tools whose names start with a server name come from the user's MCP servers. Use them when they help. The user approves each call; if they decline, carry on without it and don't ask again for the same thing.");
        prompt.AppendLine($"Today's date is {today:dddd d MMMM yyyy}.");

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            prompt.AppendLine();
            prompt.AppendLine("Additional instructions from the user:");
            prompt.AppendLine(instructions.Trim());
        }

        return prompt.ToString();
    }
}
