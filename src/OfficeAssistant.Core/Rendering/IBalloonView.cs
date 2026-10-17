namespace OfficeAssistant.Core.Rendering;

/// <summary>
/// The window that shows the assistant's balloon. The app provides it (it is drawn with
/// XAML); <see cref="AssistantController"/> tells it what to show and where.
/// </summary>
public interface IBalloonView : IDisposable
{
    /// <summary>A label (bulleted item or "See more...") was clicked.</summary>
    event EventHandler<string>? LabelClicked;

    event EventHandler<string>? ButtonClicked;

    /// <summary>Escape was pressed.</summary>
    event EventHandler? Dismissed;

    bool IsVisible { get; }

    /// <summary>Current text of the Answer Wizard box (empty while it still shows the prompt).</summary>
    string Question { get; }

    /// <summary>
    /// Shows <paramref name="content"/> next to the character. With <paramref name="activate"/>
    /// a balloon with a text box takes the keyboard focus.
    /// </summary>
    void Show(BalloonContent content, BalloonFrame frame, bool activate);

    /// <summary>Follows the character after it moved or changed size.</summary>
    void UpdateFrame(BalloonFrame frame);

    void Close();
}
