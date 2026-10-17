using OfficeAssistant.Core.Chat;

namespace AcsFormat.Tests;

public class ConversationTests
{
    private static readonly DateTime Today = new(2026, 10, 17);

    [Fact]
    public void SystemPromptPutsClaudeInCharacter()
    {
        var prompt = SystemPrompt.Build("Clippit", "A helpful paper clip.", canAnimate: true, "  Answer in Norwegian.  ", Today);

        Assert.StartsWith("You are Clippit, one of the Office Assistant characters", prompt);
        Assert.Contains("Your character description: A helpful paper clip.", prompt);
        Assert.Contains($"Use the {SystemPrompt.PlayAnimationTool} tool", prompt);
        Assert.Contains("Today's date is Saturday 17 October 2026.", prompt);
        Assert.EndsWith("Additional instructions from the user:\r\nAnswer in Norwegian.\r\n".ReplaceLineEndings(), prompt);
    }

    [Fact]
    public void SystemPromptLeavesOutWhatTheCharacterLacks()
    {
        var prompt = SystemPrompt.Build("Genius", description: null, canAnimate: false, instructions: " ", Today);

        Assert.DoesNotContain("Your character description", prompt);
        Assert.DoesNotContain(SystemPrompt.PlayAnimationTool, prompt);
        Assert.DoesNotContain("Additional instructions", prompt);
    }

    [Fact]
    public void PagerWalksThroughLongAnswersAndWraps()
    {
        var pager = new AnswerPager();
        Assert.Null(pager.Current);
        Assert.False(pager.MoveNext());

        pager.SetAnswer("**First** paragraph.\n\nSecond paragraph.\n\nThird paragraph.", pageLength: 20);

        Assert.Equal(["First paragraph.", "Second paragraph.", "Third paragraph."], pager.Pages);
        Assert.True(pager.HasMore);
        Assert.Equal("First paragraph.", pager.Current);
        Assert.True(pager.MoveNext());
        Assert.True(pager.MoveNext());
        Assert.Equal("Third paragraph.", pager.Current);
        Assert.True(pager.MoveNext());
        Assert.Equal("First paragraph.", pager.Current);

        pager.Clear();
        Assert.Null(pager.Current);
    }

    [Fact]
    public void ShortAnswerIsOnePage()
    {
        var pager = new AnswerPager();
        pager.SetAnswer("Hello!");

        Assert.False(pager.HasMore);
        Assert.False(pager.MoveNext());
        Assert.Equal("Hello!", pager.Current);
    }
}

public class CharacterAnimationsTests
{
    private static readonly Lazy<AcsCharacter> Clippit = new(() => AcsCharacter.Load(TestFiles.Assistant("CLIPPIT.ACS")));

    [Fact]
    public void GesturesLeaveOutIdlingLookingAndShowing()
    {
        var gestures = OfficeAssistant.Core.Animation.CharacterAnimations.Gestures(Clippit.Value).ToList();

        Assert.Contains("Congratulate", gestures);
        Assert.Contains("Explain", gestures);
        Assert.DoesNotContain(gestures, g => g.StartsWith("Idle", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(gestures, g => g.StartsWith("Look", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(gestures, g => g.StartsWith("Gesture", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Show", gestures);
        Assert.DoesNotContain("RestPose", gestures);
        Assert.Empty(OfficeAssistant.Core.Animation.CharacterAnimations.Gestures(null));
    }

    [Fact]
    public void IdlingGetsCalmerTheLongerTheUserIsAway()
    {
        var soon = OfficeAssistant.Core.Animation.CharacterAnimations.Idle(Clippit.Value, TimeSpan.FromSeconds(10));
        var later = OfficeAssistant.Core.Animation.CharacterAnimations.Idle(Clippit.Value, TimeSpan.FromMinutes(5));

        Assert.NotEmpty(soon);
        Assert.NotEmpty(later);
        Assert.Equal(Clippit.Value.GetStateAnimations("IDLINGLEVEL1"), soon);
        var calmest = new[] { "IDLINGLEVEL3", "IDLINGLEVEL2", "IDLINGLEVEL1" }
            .Select(Clippit.Value.GetStateAnimations)
            .First(level => level.Count > 0);
        Assert.Equal(calmest, later);
    }
}
