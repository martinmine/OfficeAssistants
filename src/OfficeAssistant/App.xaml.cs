using Microsoft.UI.Xaml;
using OfficeAssistant.Core;

namespace OfficeAssistant;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// The main application window (the Office Assistant gallery and options).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// The floating assistant. It lives in its own Win32 layered windows so it can be
    /// drawn pixel for pixel and float above every application.
    /// </summary>
    public static AssistantController Assistant { get; private set; } = null!;

    /// <summary>
    /// The native window handle (HWND) of the main window, for pickers and interop.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Assistant = new AssistantController();
        Window = new MainWindow();
        Window.Closed += (_, _) => Assistant.Dispose();
        Window.Activate();
    }
}
