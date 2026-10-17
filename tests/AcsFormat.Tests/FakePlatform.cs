using OfficeAssistant.Core.Platform;

namespace AcsFormat.Tests;

/// <summary>An in-memory platform for testing the interaction logic on any OS.</summary>
internal sealed class FakePlatform : IPlatform
{
    public FakeDisplays FakeDisplays { get; } = new();

    public FakeClipboard FakeClipboard { get; } = new();

    public List<FakeSurface> Surfaces { get; } = [];

    public IDisplays Displays => FakeDisplays;

    public IClipboard Clipboard => FakeClipboard;

    public ISecretStore Secrets { get; } = new FakeSecretStore();

    // Long enough that the caret never blinks during a test.
    public int CaretBlinkMilliseconds => 1_000_000;

    public IFloatingSurface CreateSurface(SurfaceKind kind)
    {
        var surface = new FakeSurface(kind);
        Surfaces.Add(surface);
        return surface;
    }

    public IAudioOutput CreateAudioOutput() => new FakeAudio();

    public ITextRenderer CreateTextRenderer(string face, int pixelHeight) => new FixedWidthText();
}

internal sealed class FakeSurface(SurfaceKind kind) : IFloatingSurface
{
    public event EventHandler? PointerPressed;

    public event EventHandler? PointerMoved;

    public event EventHandler? PointerReleased;

    public event EventHandler? DoubleClicked;

    public event EventHandler? SecondaryClicked;

    public event EventHandler? CaptureLost;

    public event EventHandler? Deactivated;

    public event EventHandler<KeyInput>? KeyDown;

    public event EventHandler<char>? CharacterTyped;

    public SurfaceKind Kind { get; } = kind;

    public int X { get; private set; }

    public int Y { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int Scale { get; private set; } = 1;

    public bool IsVisible { get; private set; }

    public bool IsActive { get; private set; }

    public bool HasCapture { get; private set; }

    public uint[] LastPixels { get; private set; } = [];

    public (int X, int Y) CursorPosition { get; set; }

    public KeyModifiers Modifiers { get; set; }

    public Func<CursorKind>? CursorProvider { get; set; }

    public IReadOnlyList<MenuItem>? LastMenu { get; private set; }

    public int MenuChoice { get; set; }

    public void Present(ReadOnlySpan<uint> pixels, int width, int height, int x, int y, int scale)
    {
        LastPixels = pixels.ToArray();
        (X, Y, Scale, Width, Height) = (x, y, scale, width * scale, height * scale);
    }

    public void Show(bool activate = false)
    {
        IsVisible = true;
        IsActive |= activate;
    }

    public void Hide() => IsVisible = false;

    public void MoveTo(int x, int y) => (X, Y) = (x, y);

    public void Activate() => IsActive = true;

    public void CapturePointer() => HasCapture = true;

    public void ReleasePointer()
    {
        // Like Windows: releasing raises the capture-changed notification synchronously.
        HasCapture = false;
        CaptureLost?.Invoke(this, EventArgs.Empty);
    }

    public int ShowMenu(int x, int y, IReadOnlyList<MenuItem> items)
    {
        LastMenu = items;
        return MenuChoice;
    }

    public void Dispose()
    {
    }

    public void Press(int x, int y)
    {
        CursorPosition = (x, y);
        PointerPressed?.Invoke(this, EventArgs.Empty);
    }

    public void MovePointer(int x, int y)
    {
        CursorPosition = (x, y);
        PointerMoved?.Invoke(this, EventArgs.Empty);
    }

    public void Release(int x, int y)
    {
        CursorPosition = (x, y);
        PointerReleased?.Invoke(this, EventArgs.Empty);
    }

    public void Click(int x, int y)
    {
        Press(x, y);
        Release(x, y);
    }

    public void DoubleClick(int x, int y)
    {
        CursorPosition = (x, y);
        DoubleClicked?.Invoke(this, EventArgs.Empty);
        PointerReleased?.Invoke(this, EventArgs.Empty);
    }

    public void RightClick(int x, int y)
    {
        CursorPosition = (x, y);
        SecondaryClicked?.Invoke(this, EventArgs.Empty);
    }

    public void Deactivate()
    {
        IsActive = false;
        Deactivated?.Invoke(this, EventArgs.Empty);
    }

    public void Key(Key key, KeyModifiers modifiers = KeyModifiers.None) => KeyDown?.Invoke(this, new KeyInput(key, modifiers));

    public void Type(string text)
    {
        foreach (var c in text)
        {
            CharacterTyped?.Invoke(this, c);
        }
    }
}

internal sealed class FakeDisplays : IDisplays
{
    public PixelRect WorkArea { get; set; } = new(0, 0, 1920, 1040);

    public uint Dpi { get; set; } = 96;

    public PixelRect WorkAreaAt(int x, int y) => WorkArea;

    public uint DpiAt(int x, int y) => Dpi;
}

internal sealed class FakeClipboard : IClipboard
{
    public string? Text { get; set; }

    public string? GetText() => Text;

    public void SetText(string text) => Text = text;
}

internal sealed class FakeSecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = [];

    public string Protect(string secret)
    {
        var id = $"fake:{_secrets.Count}";
        _secrets[id] = secret;
        return id;
    }

    public string? Unprotect(string stored) => _secrets.GetValueOrDefault(stored);

    public void Remove(string stored) => _secrets.Remove(stored);
}

internal sealed class FakeAudio : IAudioOutput
{
    public IAudioClip? Prepare(ReadOnlySpan<byte> wave) => null;

    public void Play(IAudioClip clip)
    {
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}

/// <summary>Every character is 6 pixels wide and draws nothing.</summary>
internal sealed class FixedWidthText : ITextRenderer
{
    public const int CharWidth = 6;

    public int Measure(string text, bool bold) => text.Length * CharWidth;

    public void Draw(PixelBuffer target, string text, int x, int y, uint color, bool bold)
    {
    }

    public void DrawWithMnemonic(PixelBuffer target, string text, int x, int y, uint color)
    {
    }

    public void Dispose()
    {
    }
}
