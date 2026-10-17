using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace OfficeAssistant.Core.Chat;

/// <summary>
/// Prepares Claude's answers for the balloon, which only draws plain wrapped text:
/// strips Markdown and splits long answers into pages for "See more...".
/// </summary>
public static class AnswerFormatter
{
    public const int DefaultPageLength = 520;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();

    /// <summary>
    /// Removes Markdown markup, keeping the text: bullets become "•", numbered items keep
    /// their numbers and links become "text (url)". Blocks are separated by a blank line
    /// only where the source had one.
    /// </summary>
    public static string ToPlainText(string markdown)
    {
        markdown = markdown.ReplaceLineEndings("\n");
        var writer = new PlainTextWriter(markdown);
        writer.WriteContainer(Markdown.Parse(markdown, Pipeline), string.Empty);
        return writer.ToString();
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

    /// <summary>Walks a parsed Markdown document and collects the balloon's plain-text lines.</summary>
    private sealed class PlainTextWriter(string source)
    {
        private readonly List<string> _lines = [];
        private readonly int[] _lineStarts = LineStarts(source);
        private int _lastSourceLine = -1;
        private string? _pendingPrefix;

        public void WriteContainer(ContainerBlock container, string indent)
        {
            foreach (var block in container)
            {
                switch (block)
                {
                    case ListBlock list:
                        foreach (var item in list.OfType<ListItemBlock>())
                        {
                            var marker = list.IsOrdered ? $"{item.Order}{list.OrderedDelimiter} " : "• ";
                            _pendingPrefix = indent + marker;
                            WriteContainer(item, indent + new string(' ', marker.Length));
                            _pendingPrefix = null;
                        }

                        break;

                    case Table table:
                        WriteLines(table, indent, table.OfType<TableRow>().Select(row =>
                            string.Join("  ", row.OfType<TableCell>().Select(CellText))));
                        break;

                    case ContainerBlock child:
                        // Block quotes and anything else that only groups blocks.
                        WriteContainer(child, indent);
                        break;

                    case LeafBlock leaf:
                        WriteLines(leaf, indent, LeafLines(leaf));
                        break;
                }
            }
        }

        public override string ToString() => string.Join("\n", _lines).Trim();

        private static IEnumerable<string> LeafLines(LeafBlock leaf) => leaf switch
        {
            // Fenced and indented code keep their lines; the fences are not part of them.
            CodeBlock code => code.Lines.Lines.Take(code.Lines.Count).Select(line => line.ToString()),
            HtmlBlock html => html.Lines.Lines.Take(html.Lines.Count).Select(line => StripTags(line.ToString())),
            ThematicBreakBlock or LinkReferenceDefinition => [],
            { Inline: { } inline } => InlineText(inline).Split('\n'),
            _ => [],
        };

        private static string CellText(TableCell cell) =>
            string.Join(" ", cell.OfType<LeafBlock>().Select(leaf => leaf.Inline is { } inline ? InlineText(inline).Trim() : string.Empty));

        private static string InlineText(ContainerInline container)
        {
            var text = new StringBuilder();
            AppendChildren(text, container);
            return text.ToString();
        }

        private static void AppendInline(StringBuilder text, Inline inline)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    text.Append(literal.Content);
                    break;
                case CodeInline code:
                    text.Append(code.Content);
                    break;
                case AutolinkInline autolink:
                    text.Append(autolink.Url);
                    break;
                case LinkInline { IsImage: true } image:
                    AppendChildren(text, image);
                    break;
                case LinkInline link:
                    var label = InlineText(link);
                    var url = link.Url ?? string.Empty;
                    text.Append(url.Length == 0 || label == url ? label : $"{label} ({url})");
                    break;
                case LineBreakInline:
                    text.Append('\n');
                    break;
                case HtmlEntityInline entity:
                    text.Append(entity.Transcoded);
                    break;
                case HtmlInline:
                    break;
                case ContainerInline container:
                    // Emphasis and other wrappers: keep only their text.
                    AppendChildren(text, container);
                    break;
            }
        }

        private static void AppendChildren(StringBuilder text, ContainerInline container)
        {
            foreach (var child in container)
            {
                AppendInline(text, child);
            }
        }

        private static string StripTags(string html)
        {
            var text = new StringBuilder(html.Length);
            var inTag = false;
            foreach (var c in html)
            {
                if (c == '<')
                {
                    inTag = true;
                }
                else if (c == '>' && inTag)
                {
                    inTag = false;
                }
                else if (!inTag)
                {
                    text.Append(c);
                }
            }

            return text.ToString();
        }

        private static int[] LineStarts(string text)
        {
            var starts = new List<int> { 0 };
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    starts.Add(i + 1);
                }
            }

            return [.. starts];
        }

        private void WriteLines(Block block, string indent, IEnumerable<string> lines)
        {
            // A blank line in the source between two blocks becomes one blank line here.
            if (_lines.Count > 0 && block.Line > _lastSourceLine + 1)
            {
                AddLine(string.Empty);
            }

            foreach (var line in lines)
            {
                var prefix = _pendingPrefix ?? indent;
                _pendingPrefix = null;
                AddLine(line.Length == 0 ? string.Empty : prefix + line);
            }

            _lastSourceLine = Math.Max(_lastSourceLine, SourceLine(block.Span.End));
        }

        private void AddLine(string line)
        {
            line = line.TrimEnd();
            if (line.Length == 0 && (_lines.Count == 0 || _lines[^1].Length == 0))
            {
                return;
            }

            _lines.Add(line);
        }

        private int SourceLine(int position)
        {
            var index = Array.BinarySearch(_lineStarts, Math.Max(0, position));
            return index >= 0 ? index : ~index - 1;
        }
    }
}
