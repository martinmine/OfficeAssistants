using AcsFormat;

namespace OfficeAssistant.Core.Animation;

/// <summary>Which of a character's animations to play for gestures and idling.</summary>
internal static class CharacterAnimations
{
    private static readonly string[] NonGestureAnimations = ["Show", "Hide", "RestPose", "Hearing_1", "Alert", "GoodBye"];

    /// <summary>Animations that make sense as a reaction (not idling, looking or showing).</summary>
    public static IEnumerable<string> Gestures(AcsCharacter? character) =>
        character?.Animations
            .Select(a => a.Name)
            .Where(name => !name.StartsWith("Idle", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("Look", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("Gesture", StringComparison.OrdinalIgnoreCase)
                && !NonGestureAnimations.Contains(name, StringComparer.OrdinalIgnoreCase))
        ?? [];

    /// <summary>
    /// Idle animations for how long the user has been away: level 1 under a minute, level 2
    /// under three minutes, then level 3, falling back to a calmer level the character has.
    /// </summary>
    public static IReadOnlyList<string> Idle(AcsCharacter character, TimeSpan idleFor)
    {
        var level = idleFor.TotalSeconds < 60 ? 1 : idleFor.TotalSeconds < 180 ? 2 : 3;
        IReadOnlyList<string> animations = [];
        for (; level >= 1 && animations.Count == 0; level--)
        {
            animations = character.GetStateAnimations($"IDLINGLEVEL{level}");
        }

        return animations;
    }

    /// <summary>How long to wait before the next idle animation, in milliseconds.</summary>
    public static (int Min, int Max) IdleDelay(AcsCharacter character) =>
        // Gallery-only assistants have nothing but their wave, so idle less often.
        character.Animations.Count > 3 ? (6_000, 12_000) : (25_000, 45_000);
}
