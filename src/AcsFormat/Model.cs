namespace AcsFormat;

[Flags]
public enum AcsCharacterFlags : uint
{
    None = 0,
    VoiceOutputDisabled = 0x0000_0010,
    VoiceOutputEnabled = 0x0000_0020,
    BalloonDisabled = 0x0000_0100,
    BalloonEnabled = 0x0000_0200,
    BalloonSizeToText = 0x0001_0000,
    BalloonAutoHideDisabled = 0x0002_0000,
    BalloonAutoPaceDisabled = 0x0004_0000,
    StandardAnimationSet = 0x0010_0000,
}

/// <summary>Text-to-speech settings stored with the character (unused by Office assistants).</summary>
public sealed record AcsVoiceInfo(
    Guid EngineId,
    Guid ModeId,
    uint Speed,
    ushort Pitch,
    ushort? LanguageId,
    string? Dialect,
    ushort? Gender,
    ushort? Age,
    string? Style);

/// <summary>Microsoft Agent word balloon defaults. Colors are 0xAARRGGBB.</summary>
public sealed record AcsBalloonInfo(
    int Lines,
    int CharactersPerLine,
    uint ForegroundColor,
    uint BackgroundColor,
    uint BorderColor,
    string FontName,
    int FontHeight,
    int FontWeight,
    bool Italic);

/// <summary>
/// Name, description and Office-specific extra strings for one language.
/// </summary>
public sealed class AcsLocalizedInfo
{
    internal AcsLocalizedInfo(ushort languageId, string name, string description, string extraData)
    {
        LanguageId = languageId;
        Name = name;
        Description = description;
        ExtraData = extraData;

        // Office stores its own strings in the extra data: sections separated by "^^",
        // entries within a section separated by "~~". Section 0 holds the greetings shown
        // when the assistant is opened, section 1 the reminder captions.
        var sections = extraData.Split("^^");
        Greetings = SplitEntries(sections.ElementAtOrDefault(0));
        Reminders = SplitEntries(sections.ElementAtOrDefault(1));
    }

    /// <summary>Windows language identifier (a LANGID; primary-only ids such as 0x09 act as fallbacks).</summary>
    public ushort LanguageId { get; }

    public string Name { get; }

    public string Description { get; }

    /// <summary>Raw extra data string as stored in the file.</summary>
    public string ExtraData { get; }

    public IReadOnlyList<string> Greetings { get; }

    public IReadOnlyList<string> Reminders { get; }

    public override string ToString() => $"0x{LanguageId:X4} {Name}";

    private static string[] SplitEntries(string? section) =>
        string.IsNullOrEmpty(section)
            ? []
            : section.Split("~~", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public enum AcsTransitionType : byte
{
    /// <summary>Play <see cref="AcsAnimation.ReturnAnimation"/> when the animation finishes.</summary>
    ReturnAnimation = 0,

    /// <summary>Leave the animation by following the frames' exit branches.</summary>
    ExitBranches = 1,

    None = 2,
}

public sealed record AcsAnimation(
    string Name,
    AcsTransitionType TransitionType,
    string ReturnAnimation,
    IReadOnlyList<AcsFrame> Frames)
{
    public override string ToString() => $"{Name} ({Frames.Count} frames)";
}

public sealed record AcsFrame(
    IReadOnlyList<AcsFrameImage> Images,
    int SoundIndex,
    int Duration,
    int ExitFrame,
    IReadOnlyList<AcsBranch> Branches,
    IReadOnlyList<AcsOverlay> Overlays)
{
    /// <summary>Frame duration in milliseconds (stored in hundredths of a second).</summary>
    public int DurationMilliseconds => Duration * 10;

    public bool HasSound => SoundIndex >= 0;
}

/// <summary>An image drawn in a frame, offset from the frame's top-left corner.</summary>
public readonly record struct AcsFrameImage(int ImageIndex, int X, int Y);

/// <summary>Branch to <see cref="FrameIndex"/> with the given percent probability.</summary>
public readonly record struct AcsBranch(int FrameIndex, int Probability);

public enum AcsMouthShape : byte
{
    Closed = 0,
    Wide1 = 1,
    Wide2 = 2,
    Wide3 = 3,
    Wide4 = 4,
    Medium = 5,
    Narrow = 6,
}

public sealed record AcsOverlay(
    AcsMouthShape Shape,
    bool ReplacesTopImage,
    int ImageIndex,
    int X,
    int Y,
    int Width,
    int Height);

/// <summary>A RIFF/WAVE sound clip referenced by frames.</summary>
public sealed class AcsSound(byte[] waveData)
{
    public ReadOnlyMemory<byte> WaveData { get; } = waveData;
}
