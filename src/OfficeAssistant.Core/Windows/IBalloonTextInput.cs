namespace OfficeAssistant.Core.Windows;

/// <summary>Where the balloon's text box is on screen, so the text input can sit exactly over it.</summary>
/// <param name="X">Left edge of the box's border, in physical pixels.</param>
/// <param name="Y">Top edge of the box's border, in physical pixels.</param>
/// <param name="Width">Width including the border, in physical pixels.</param>
/// <param name="Height">Height including the border, in physical pixels.</param>
/// <param name="Scale">The balloon's pixel size: one balloon pixel is this many screen pixels.</param>
/// <param name="Dpi">Effective DPI of the monitor the box is on.</param>
/// <param name="Owner">The balloon window, which the text input stays above.</param>
public readonly record struct BalloonTextBoxBounds(int X, int Y, int Width, int Height, int Scale, uint Dpi, nint Owner);

/// <summary>
/// A real text box that the app shows over the balloon's GDI-drawn box while the user types,
/// so typing gets IME composition, the emoji panel, spell checking and UI Automation. When
/// editing ends the balloon draws the text itself again, pixel for pixel like Office XP.
/// </summary>
public interface IBalloonTextInput : IDisposable
{
    /// <summary>Enter was pressed.</summary>
    event EventHandler? Submitted;

    /// <summary>Escape was pressed.</summary>
    event EventHandler? Dismissed;

    /// <summary>The text box lost the keyboard focus (or was hidden).</summary>
    event EventHandler? EditingEnded;

    bool IsEditing { get; }

    /// <summary>The text being edited.</summary>
    string Text { get; }

    /// <summary>Shows the text box over <paramref name="bounds"/> with <paramref name="text"/> and gives it the keyboard focus.</summary>
    void BeginEditing(BalloonTextBoxBounds bounds, string text, bool selectAll);

    /// <summary>Follows the balloon after it moved.</summary>
    void Move(BalloonTextBoxBounds bounds);

    /// <summary>Hides the text box without raising <see cref="EditingEnded"/>.</summary>
    void EndEditing();
}
