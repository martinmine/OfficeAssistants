namespace OfficeAssistant.Core.Rendering;

/// <summary>
/// Balloons recreated from the Office XP reference screenshots.
/// </summary>
public static class BalloonScenarios
{
    public const string SearchButton = "search";
    public const string OptionsButton = "options";
    public const string SeeMoreId = "seemore";
    public const string QuestionPlaceholder = "Type your question here and then click Search.";

    private static readonly BalloonButtons AnswerWizardButtons =
        new([new BalloonButton("&Options", OptionsButton), new BalloonButton("&Search", SearchButton)]) { DefaultIndex = 1 };

    /// <summary>"What would you like to do?" shown when the assistant is clicked.</summary>
    public static BalloonContent AnswerWizard() => new(
        new BalloonText("What would you like to do?", Bold: true),
        new BalloonTextBox(QuestionPlaceholder) { IsPlaceholder = true },
        AnswerWizardButtons);

    /// <summary>A tip followed by the Answer Wizard, as in the AutoComplete screenshot.</summary>
    public static BalloonContent TipWithAnswerWizard(string tip) => new(
        new BalloonText(tip),
        new BalloonText("What would you like to do?", Bold: true),
        new BalloonTextBox(QuestionPlaceholder) { IsPlaceholder = true, SpaceBefore = 3 },
        AnswerWizardButtons);

    /// <summary>Answer Wizard results: up to five topics and "See more...".</summary>
    public static BalloonContent SearchResults(IReadOnlyList<(string Id, string Title)> topics, bool hasMore)
    {
        var blocks = new List<BalloonBlock> { new BalloonText("What would you like to do?", Bold: true) };
        blocks.AddRange(topics.Select(t => new BalloonLabel(t.Title, t.Id)));
        if (hasMore)
        {
            blocks.Add(new BalloonSeeMore("See more...", SeeMoreId));
        }

        blocks.Add(new BalloonTextBox(string.Empty) { SpaceBefore = 16 });
        blocks.Add(AnswerWizardButtons);
        return new BalloonContent([.. blocks]);
    }

    /// <summary>The Word "save changes" alert routed through the assistant.</summary>
    public static BalloonContent SaveChanges(string application = "Microsoft Word", string document = "Document1") => new(
        new BalloonIconHeading(BalloonIcon.Warning, application),
        new BalloonText($"Do you want to save the changes to {document}?"),
        new BalloonSeparator(),
        new BalloonButtons([new BalloonButton("&Yes", "yes"), new BalloonButton("&No", "no"), new BalloonButton("Cancel", "cancel")])
        {
            FocusedIndex = 0,
            DefaultIndex = 0,
        });

    /// <summary>The Letter Wizard tip.</summary>
    public static BalloonContent LetterTip() => new(
        new BalloonText("It looks like you're writing a letter."),
        new BalloonText("Would you like help?"),
        new BalloonLabel("Get help with writing the letter", "letter-help"),
        new BalloonLabel("Just type the letter without help", "letter-nohelp"),
        new BalloonSeparator(),
        new BalloonButtons([new BalloonButton("Cancel", "cancel")]) { DefaultIndex = 0 });

    /// <summary>A plain message with an OK button.</summary>
    public static BalloonContent Message(string text, string? heading = null)
    {
        var blocks = new List<BalloonBlock>();
        if (heading is not null)
        {
            blocks.Add(new BalloonText(heading, Bold: true));
        }

        blocks.Add(new BalloonText(text));
        blocks.Add(new BalloonButtons([new BalloonButton("OK", "ok")]) { DefaultIndex = 0, SpaceBefore = 16 });
        return new BalloonContent([.. blocks]);
    }
}
