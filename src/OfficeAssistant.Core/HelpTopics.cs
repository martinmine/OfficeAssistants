namespace OfficeAssistant.Core;

/// <summary>
/// The canned help index from the original Answer Wizard prototype. The assistant now answers
/// with Claude; this is kept to recreate the Office XP search results balloon.
/// </summary>
public static class HelpTopics
{
    public sealed record Topic(string Id, string Title, string Animation);

    public static IReadOnlyList<Topic> All { get; } =
    [
        new("print-merge", "Print merged documents", "Print"),
        new("print-cancel", "Cancel printing", "Print"),
        new("print-file", "Print a document to a file", "Print"),
        new("print", "Print a document", "Print"),
        new("print-web", "WEB: Printing troubleshooter", "Searching"),
        new("print-envelope", "Print an envelope", "Print"),
        new("print-labels", "Print labels for a mass mailing", "Print"),
        new("save", "Save a document", "Save"),
        new("save-format", "Save a document in another file format", "Save"),
        new("save-auto", "Recover documents after a crash", "CheckingSomething"),
        new("mail", "Send a document in an e-mail message", "SendMail"),
        new("mail-merge", "Create a mail merge", "SendMail"),
        new("letter", "Write a letter", "Writing"),
        new("letter-wizard", "Use the Letter Wizard", "GetWizardy"),
        new("table", "Create a table", "GetTechy"),
        new("spelling", "Check spelling and grammar", "CheckingSomething"),
        new("find", "Find and replace text", "Searching"),
        new("picture", "Insert a picture", "GetArtsy"),
        new("drawing", "Draw shapes and lines", "GetArtsy"),
        new("format", "Format text", "Writing"),
        new("autocomplete", "Turn AutoComplete on or off", "Thinking"),
        new("delete", "Delete a file", "EmptyTrash"),
        new("assistant-change", "Choose a different Office Assistant", "GetWizardy"),
        new("assistant-hide", "Hide or turn off the Office Assistant", "GoodBye"),
        new("assistant-sounds", "Turn Office Assistant sounds on or off", "Explain"),
    ];

    /// <summary>Topics containing any word of the question, best matches first.</summary>
    public static IReadOnlyList<Topic> Search(string question)
    {
        var words = question
            .Split([' ', ',', '.', '?', '!', ';', ':'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .Select(w => w.ToLowerInvariant())
            .ToArray();
        if (words.Length == 0)
        {
            return [];
        }

        return All
            .Select(t => (Topic: t, Score: words.Count(w => t.Title.Contains(w, StringComparison.OrdinalIgnoreCase))))
            .Where(r => r.Score > 0)
            .OrderByDescending(r => r.Score)
            .Select(r => r.Topic)
            .ToList();
    }
}
