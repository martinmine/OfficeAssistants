using System.Runtime.InteropServices.WindowsRuntime;
using AcsFormat;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using OfficeAssistant.Core.Animation;
using OfficeAssistant.ViewModels;

namespace OfficeAssistant;

/// <summary>
/// The gallery and options page. The gallery preview plays the assistant's gallery
/// animation in a bitmap shown at one image pixel per physical screen pixel.
/// </summary>
public sealed partial class MainPage : Page
{
    private const int PreviewPauseMilliseconds = 1500;

    private readonly UiTimer _replayTimer = new();
    private AnimationPlayer? _previewPlayer;
    private SoundPlayer? _previewSounds;
    private WriteableBitmap? _previewBitmap;
    private byte[] _previewPixels = [];
    private AcsCharacter? _previewCharacter;
    private uint[]? _lastPreviewFrame;

    public MainPage()
    {
        InitializeComponent();
        ViewModel.GalleryChanged += (_, _) => StartPreview();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainPageViewModel.ScaleIndex))
            {
                RedrawPreview();
            }
            else if (e.PropertyName == nameof(MainPageViewModel.PreviewMuted) && _previewSounds is not null)
            {
                _previewSounds.Enabled = !ViewModel.PreviewMuted;
                if (ViewModel.PreviewMuted)
                {
                    _previewSounds.Stop();
                }
            }
        };

        App.Assistant.ChooseAssistantRequested += (_, _) => (App.Window as MainWindow)?.BringToFront();
        App.Assistant.OptionsRequested += (_, _) => (App.Window as MainWindow)?.BringToFront();

        Loaded += OnLoaded;
        Unloaded += (_, _) => StopPreview();

        // Here rather than in Loaded: the window stays hidden on launch once settings are saved.
        ViewModel.Initialize([Path.Combine(AppContext.BaseDirectory, "Assistants")]);
        if (!ViewModel.HasAssistants)
        {
            ViewModel.GalleryName = "No assistant files found";
            ViewModel.GalleryDescription = "Add .acs or .acg files to get started.";
        }
    }

    public MainPageViewModel ViewModel { get; } = new();

    /// <summary>The preview uses the assistant's pixel size, including the automatic one.</summary>
    private static int PreviewScale => App.Assistant.Scale;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        XamlRoot.Changed += (_, _) => UpdatePreviewSize();

        // Don't keep the gallery preview (and its sounds) running while the window is hidden.
        App.Window.AppWindow.Changed += (window, change) =>
        {
            if (change.DidVisibilityChange)
            {
                if (window.IsVisible)
                {
                    StartPreview();
                }
                else
                {
                    StopPreview();
                }
            }
        };
        StartPreview();
    }

    private void StartPreview()
    {
        StopPreview();
        if (App.Window?.AppWindow.IsVisible != true)
        {
            return;
        }

        _previewCharacter = ViewModel.GalleryCharacter;
        if (_previewCharacter is null)
        {
            return;
        }

        _previewSounds = new SoundPlayer(_previewCharacter, App.Assistant.Platform.CreateAudioOutput()) { Enabled = !ViewModel.PreviewMuted };
        _previewPlayer = new AnimationPlayer(_previewCharacter, ShowPreviewFrame, _previewSounds.Play);
        PlayPreview();
    }

    private void PlayPreview()
    {
        if (_previewPlayer is null || _previewCharacter is null)
        {
            return;
        }

        // The gallery files hold a single "Wave"; full characters also have one.
        var animation = _previewCharacter.FindAnimation("Wave")?.Name ?? _previewCharacter.Animations.FirstOrDefault()?.Name;
        if (animation is null)
        {
            return;
        }

        _previewPlayer.Play(animation, () => _replayTimer.Start(PreviewPauseMilliseconds, PlayPreview));
    }

    private void StopPreview()
    {
        _replayTimer.Stop();
        _previewPlayer?.Abort();
        _previewPlayer?.Dispose();
        _previewSounds?.Dispose();
        _previewPlayer = null;
        _previewSounds = null;
    }

    private void ShowPreviewFrame(uint[] frame)
    {
        _lastPreviewFrame = frame;
        RedrawPreview();
    }

    private void RedrawPreview()
    {
        if (_previewCharacter is null || _lastPreviewFrame is null)
        {
            return;
        }

        var scale = PreviewScale;
        var width = _previewCharacter.Width * scale;
        var height = _previewCharacter.Height * scale;
        if (_previewBitmap is null || _previewBitmap.PixelWidth != width || _previewBitmap.PixelHeight != height)
        {
            _previewBitmap = new WriteableBitmap(width, height);
            PreviewImage.Source = _previewBitmap;
            UpdatePreviewSize();
        }

        // Reused from frame to frame while the size stays the same.
        if (_previewPixels.Length != width * height * 4)
        {
            _previewPixels = new byte[width * height * 4];
        }

        var pixels = _previewPixels;
        var source = _lastPreviewFrame;
        var sourceWidth = _previewCharacter.Width;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = source[((y / scale) * sourceWidth) + (x / scale)];
                var i = ((y * width) + x) * 4;
                pixels[i] = (byte)p;
                pixels[i + 1] = (byte)(p >> 8);
                pixels[i + 2] = (byte)(p >> 16);
                pixels[i + 3] = (byte)(p >> 24);
            }
        }

        using (var stream = _previewBitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels);
        }

        _previewBitmap.Invalidate();
    }

    /// <summary>Sizes the image so each bitmap pixel lands on exactly one screen pixel.</summary>
    private void UpdatePreviewSize()
    {
        if (_previewBitmap is null || XamlRoot is null)
        {
            return;
        }

        var rasterization = XamlRoot.RasterizationScale;
        PreviewImage.Width = _previewBitmap.PixelWidth / rasterization;
        PreviewImage.Height = _previewBitmap.PixelHeight / rasterization;
    }

    private void Animation_ItemClick(object sender, ItemClickEventArgs e) =>
        ViewModel.PlayAnimationCommand.Execute(e.ClickedItem as string);

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.FileTypeFilter.Add(".acs");
        picker.FileTypeFilter.Add(".acg");

        var files = await picker.PickMultipleFilesAsync();
        if (files.Count > 0)
        {
            ViewModel.AddFiles(files.Select(f => f.Path));
        }
    }
}
