using Microsoft.UI.Xaml;

namespace OfficeAssistant;

/// <summary>
/// The application window. Hosts <see cref="MainPage"/>, which holds the UI.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 820));

        RootFrame.Navigate(typeof(MainPage));

        AppWindow.Closing += OnClosing;
    }

    /// <summary>Brings the window to the front, e.g. from the assistant's context menu.</summary>
    public void BringToFront()
    {
        if (!AppWindow.IsVisible)
        {
            AppWindow.Show();
        }

        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
    }

    /// <summary>
    /// While the assistant is on screen, closing only hides this window; the assistant's
    /// Options menu brings it back. With no assistant shown, closing exits the app.
    /// </summary>
    private void OnClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (App.Assistant.IsOnScreen)
        {
            args.Cancel = true;
            sender.Hide();
        }
    }
}
