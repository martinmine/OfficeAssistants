namespace OfficeAssistant.Core.Rendering;

/// <summary>
/// Content of an Office XP style assistant balloon, modelled on the Office
/// <c>Balloon</c> object: a vertical stack of blocks.
/// </summary>
public sealed class BalloonContent
{
    public BalloonContent(params BalloonBlock[] blocks)
    {
        Blocks = blocks;
    }

    public IReadOnlyList<BalloonBlock> Blocks { get; }

    /// <summary>Arbitrary data for the code that shows the balloon.</summary>
    public object? Tag { get; init; }
}

/// <summary>Base for a balloon block. <see cref="SpaceBefore"/> overrides the default gap above it.</summary>
public abstract record BalloonBlock
{
    public int? SpaceBefore { get; init; }
}

/// <summary>A wrapped paragraph; bold paragraphs are headings.</summary>
public sealed record BalloonText(string Text, bool Bold = false) : BalloonBlock;

public enum BalloonIcon
{
    Warning,
}

/// <summary>Message-box style heading: a 32x32 icon with bold text aligned to its bottom.</summary>
public sealed record BalloonIconHeading(BalloonIcon Icon, string Text) : BalloonBlock;

/// <summary>A clickable bulleted item (Office "label") drawn in navy.</summary>
public sealed record BalloonLabel(string Text, string Id) : BalloonBlock;

/// <summary>The "See more..." item with its arrow.</summary>
public sealed record BalloonSeeMore(string Text, string Id) : BalloonBlock;

/// <summary>The Answer Wizard question box.</summary>
public sealed record BalloonTextBox(string InitialText) : BalloonBlock;

/// <summary>Thin grey rule, used above message-box buttons.</summary>
public sealed record BalloonSeparator : BalloonBlock;

/// <summary>A row of push buttons. Use '&amp;' to mark the mnemonic.</summary>
public sealed record BalloonButtons(IReadOnlyList<BalloonButton> Buttons) : BalloonBlock
{
    /// <summary>Index of the button that shows a focus rectangle, or -1.</summary>
    public int FocusedIndex { get; init; } = -1;

    /// <summary>Index of the button triggered by Enter, or -1.</summary>
    public int DefaultIndex { get; init; } = -1;
}

public sealed record BalloonButton(string Text, string Id);
