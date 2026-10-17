using AcsFormat.Tests;
using OfficeAssistant.Core;
using OfficeAssistant.Core.Rendering;

namespace OfficeAssistant.Windows.Tests;

/// <summary>
/// Renders the balloons from the reference screenshots and requires every pixel of the
/// balloon (box, tail, text, icons, buttons) to match.
/// </summary>
public class BalloonRendererTests
{
    [WindowsFact]
    public void AnswerWizardMatchesScreenshot()
    {
        var state = new BalloonState { TextBoxText = BalloonScenarios.QuestionPlaceholder };
        AssertMatches("what would you like to do.png", BalloonScenarios.AnswerWizard(), state, tailX: 91, left: 15, top: 7, width: 185, height: 114);
    }

    [WindowsFact]
    public void AutoCompleteTipMatchesScreenshot()
    {
        var content = BalloonScenarios.TipWithAnswerWizard("Word can complete things that you frequently type. Just press enter to accept the completion.");
        var state = new BalloonState { TextBoxText = BalloonScenarios.QuestionPlaceholder, TextBoxFocused = true };
        state.SelectAll();
        AssertMatches("Screenshot 2026-10-15 204610.png", content, state, tailX: 90, left: 4, top: 7, width: 184, height: 197);
    }

    [WindowsFact]
    public void SaveChangesAlertMatchesScreenshot()
    {
        AssertMatches("Screenshot 2026-10-15 204312.png", BalloonScenarios.SaveChanges(), new BalloonState(), tailX: 122, left: 17, top: 9, width: 216, height: 122);
    }

    [WindowsFact]
    public void SearchResultsMatchScreenshot()
    {
        var topics = HelpTopics.Search("Print");
        var page = topics.Take(5).Select(t => (t.Id, t.Title)).ToList();
        var state = new BalloonState { TextBoxText = "Print", TextBoxFocused = true };
        state.SelectAll();
        AssertMatches("Screenshot 2026-10-15 204134.png", BalloonScenarios.SearchResults(page, hasMore: topics.Count > 5), state, tailX: 106, left: 25, top: 33, width: 200, height: 258);
    }

    [WindowsFact]
    public void HitTestingFindsLabelsAndButtons()
    {
        using var renderer = TestFiles.CreateBalloonRenderer();
        var state = new BalloonState();
        var layout = renderer.Measure(BalloonScenarios.SaveChanges(), state);
        var bitmap = renderer.Render(layout, state, BalloonTailSide.Bottom, 122);

        Assert.Equal(new BalloonHit(BalloonHitKind.Button, "yes"), bitmap.HitTest(30, 100));
        Assert.Equal(new BalloonHit(BalloonHitKind.Button, "cancel"), bitmap.HitTest(180, 100));
        Assert.Equal(BalloonHit.None, bitmap.HitTest(100, 50));
    }

    private static void AssertMatches(string screenshot, BalloonContent content, BalloonState state, int tailX, int left, int top, int width, int height)
    {
        using var renderer = TestFiles.CreateBalloonRenderer();
        var layout = renderer.Measure(content, state);
        Assert.Equal((width, height), (layout.Width, layout.Height));

        var bitmap = renderer.Render(layout, state, BalloonTailSide.Bottom, tailX);
        var (compared, different) = TestFiles.Compare(screenshot, bitmap.Pixels, bitmap.Width, bitmap.Height, left, top);

        Assert.True(compared > width * height * 9 / 10, $"Only {compared} pixels overlapped the screenshot.");
        Assert.Equal(0, different);
    }
}
