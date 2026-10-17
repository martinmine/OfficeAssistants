namespace OfficeAssistant.Core.Chat;

/// <summary>The pages of the last answer and which one the balloon shows ("See more...").</summary>
internal sealed class AnswerPager
{
    public IReadOnlyList<string> Pages { get; private set; } = [];

    public int Index { get; private set; }

    public bool HasMore => Pages.Count > 1;

    public string? Current => Pages.Count == 0 ? null : Pages[Index];

    /// <summary>Turns Claude's Markdown answer into balloon pages and goes to the first.</summary>
    public void SetAnswer(string markdown, int pageLength = AnswerFormatter.DefaultPageLength)
    {
        Pages = AnswerFormatter.Paginate(AnswerFormatter.ToPlainText(markdown), pageLength);
        Index = 0;
    }

    /// <summary>Goes to the next page, wrapping to the first; false when there is only one.</summary>
    public bool MoveNext()
    {
        if (!HasMore)
        {
            return false;
        }

        Index = (Index + 1) % Pages.Count;
        return true;
    }

    public void Clear()
    {
        Pages = [];
        Index = 0;
    }
}
