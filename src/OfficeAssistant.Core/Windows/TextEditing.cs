namespace OfficeAssistant.Core.Windows;

/// <summary>Word boundaries for the balloon text box, following the Windows edit control.</summary>
public static class TextEditing
{
    /// <summary>
    /// The word at <paramref name="index"/> plus its trailing spaces, or the run of
    /// spaces when the index is on whitespace. Returns (start, end).
    /// </summary>
    public static (int Start, int End) WordAt(string text, int index)
    {
        if (text.Length == 0)
        {
            return (0, 0);
        }

        index = Math.Clamp(index, 0, text.Length - 1);
        var onSpace = char.IsWhiteSpace(text[index]);
        var start = index;
        while (start > 0 && char.IsWhiteSpace(text[start - 1]) == onSpace)
        {
            start--;
        }

        var end = index;
        while (end < text.Length && char.IsWhiteSpace(text[end]) == onSpace)
        {
            end++;
        }

        if (!onSpace)
        {
            while (end < text.Length && char.IsWhiteSpace(text[end]))
            {
                end++;
            }
        }

        return (start, end);
    }

    /// <summary>Start of the next word after <paramref name="index"/> (Ctrl+Right).</summary>
    public static int NextWordStart(string text, int index)
    {
        var i = index;
        while (i < text.Length && !char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        return i;
    }

    /// <summary>Start of the word before <paramref name="index"/> (Ctrl+Left).</summary>
    public static int PreviousWordStart(string text, int index)
    {
        var i = Math.Min(index, text.Length);
        while (i > 0 && char.IsWhiteSpace(text[i - 1]))
        {
            i--;
        }

        while (i > 0 && !char.IsWhiteSpace(text[i - 1]))
        {
            i--;
        }

        return i;
    }
}
