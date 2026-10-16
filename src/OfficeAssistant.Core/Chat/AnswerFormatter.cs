using System.Text;
using System.Text.RegularExpressions;

namespace OfficeAssistant.Core.Chat;

/// <summary>
/// Prepares Claude's answers for the balloon, which only draws plain wrapped text:
/// strips Markdown and splits long answers into pages for "See more...".
/// </summary>
public static partial class AnswerFormatter
{
    public const int DefaultPageLength = 520;

    /// <summary>Removes Markdown markup, keeping the text (and bullets as "•").</summary>
    public static string ToPlainText(string markdown)
    {
        var lines = new List<string>();
        var inCode = false;
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                continue;
            }

            if (!inCode)
            {
                if (HorizontalRule().IsMatch(line) || TableSeparator().IsMatch(line))
                {
                    continue;
                }

                line = Heading().Replace(line, string.Empty);
                line = Quote().Replace(line, string.Empty);
                line = Bullet().Replace(line, "$1• ");
                line = Image().Replace(line, "$1");
                line = Link().Replace(line, m => m.Groups[1].Value == m.Groups[2].Value ? m.Groups[1].Value : $"{m.Groups[1].Value} ({m.Groups[2].Value})");
                line = Emphasis().Replace(line, "$2");
                line = UnderscoreEmphasis().Replace(line, "$2");
                line = InlineCode().Replace(line, "$1");
                if (line.TrimStart().StartsWith('|'))
                {
                    line = string.Join("  ", line.Trim().Trim('|').Split('|').Select(c => c.Trim()));
                }
            }

            lines.Add(line);
        }

        var text = string.Join("\n", lines);
        return MultipleBlankLines().Replace(text, "\n\n").Trim();
    }

    /// <summary>Splits text into pages of about <paramref name="pageLength"/> characters, at paragraph or sentence ends.</summary>
    public static IReadOnlyList<string> Paginate(string text, int pageLength = DefaultPageLength)
    {
        var pages = new List<string>();
        var page = new StringBuilder();
        foreach (var paragraph in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var piece in SplitLong(paragraph, pageLength))
            {
                if (page.Length > 0 && page.Length + 2 + piece.Length > pageLength)
                {
                    pages.Add(page.ToString());
                    page.Clear();
                }

                if (page.Length > 0)
                {
                    page.Append("\n\n");
                }

                page.Append(piece);
            }
        }

        if (page.Length > 0)
        {
            pages.Add(page.ToString());
        }

        return pages.Count > 0 ? pages : [string.Empty];
    }

    /// <summary>Breaks a paragraph that is longer than a page at sentence (or word) ends.</summary>
    private static IEnumerable<string> SplitLong(string paragraph, int pageLength)
    {
        var rest = paragraph;
        while (rest.Length > pageLength)
        {
            var window = rest[..pageLength];
            var cut = Math.Max(window.LastIndexOf(". ", StringComparison.Ordinal), Math.Max(window.LastIndexOf("? ", StringComparison.Ordinal), window.LastIndexOf("! ", StringComparison.Ordinal)));
            cut = cut > pageLength / 3 ? cut + 1 : window.LastIndexOfAny([' ', '\n']);
            if (cut <= 0)
            {
                cut = pageLength;
            }

            yield return rest[..cut].Trim();
            rest = rest[cut..].TrimStart();
        }

        if (rest.Length > 0)
        {
            yield return rest;
        }
    }

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s{0,3}>\s?")]
    private static partial Regex Quote();

    [GeneratedRegex(@"^(\s*)[-*+]\s+")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$")]
    private static partial Regex HorizontalRule();

    [GeneratedRegex(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$")]
    private static partial Regex TableSeparator();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(\*\*|\*|~~)(?=\S)(.+?)(?<=\S)\1")]
    private static partial Regex Emphasis();

    // Only at word boundaries, so snake_case names survive.
    [GeneratedRegex(@"(?<!\w)(__|_)(?=\S)(.+?)(?<=\S)\1(?!\w)")]
    private static partial Regex UnderscoreEmphasis();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleBlankLines();
}
