namespace OfficeAssistant.Core.Platform;

/// <summary>The keys the balloon's text box reacts to; everything else is <see cref="Other"/>.</summary>
public enum Key
{
    Other,
    Escape,
    Left,
    Right,
    Home,
    End,
    Delete,
    A,
    C,
    V,
    X,
}

/// <summary>
/// Modifiers in platform-neutral terms. The platform maps its conventions onto them: on
/// Windows Ctrl is both <see cref="Command"/> and <see cref="Word"/>; on macOS ⌘ is
/// <see cref="Command"/> and ⌥ is <see cref="Word"/>.
/// </summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,

    /// <summary>Shortcuts such as select all, copy, cut and paste.</summary>
    Command = 2,

    /// <summary>Arrow keys move by word.</summary>
    Word = 4,
}

public readonly record struct KeyInput(Key Key, KeyModifiers Modifiers);
