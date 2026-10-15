using System.Collections.ObjectModel;
using System.Globalization;
using AcsFormat;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OfficeAssistant.Core;
using OfficeAssistant.Core.Rendering;

namespace OfficeAssistant.ViewModels;

public sealed record LanguageOption(int Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// State for the gallery (choosing an assistant, like Office's "Gallery" tab) and the
/// options and test panels for the assistant on screen.
/// </summary>
public partial class MainPageViewModel : ObservableObject
{
    private readonly AssistantController _assistant;
    private readonly Dictionary<string, AcsCharacter> _galleryCache = [];
    private IReadOnlyList<AssistantDefinition> _assistants = [];
    private bool _updatingLanguage;

    public MainPageViewModel()
        : this(App.Assistant)
    {
    }

    public MainPageViewModel(AssistantController assistant)
    {
        _assistant = assistant;
        _assistant.VisibilityChanged += (_, _) => IsAssistantShown = _assistant.IsShown;
        _assistant.AnimationStarted += (_, name) => CurrentAnimation = name;
    }

    /// <summary>Raised when the gallery shows a different assistant (the page restarts the preview).</summary>
    public event EventHandler? GalleryChanged;

    public ObservableCollection<LanguageOption> Languages { get; } = [];

    public ObservableCollection<string> Animations { get; } = [];

    public ObservableCollection<string> Greetings { get; } = [];

    public ObservableCollection<string> Reminders { get; } = [];

    [ObservableProperty]
    public partial int GalleryIndex { get; set; } = -1;

    [ObservableProperty]
    public partial AcsCharacter? GalleryCharacter { get; set; }

    [ObservableProperty]
    public partial string GalleryName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GalleryDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GalleryPosition { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool GalleryIsPreviewOnly { get; set; }

    [ObservableProperty]
    public partial string ActiveName { get; set; } = "None";

    [ObservableProperty]
    public partial string ActiveFiles { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ActiveIsPreviewOnly { get; set; }

    [ObservableProperty]
    public partial string CurrentAnimation { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsAssistantShown { get; set; }

    [ObservableProperty]
    public partial bool SoundsEnabled { get; set; } = true;

    /// <summary>0 = original pixels, 1 = 2x, 2 = 3x.</summary>
    [ObservableProperty]
    public partial int ScaleIndex { get; set; }

    [ObservableProperty]
    public partial int LanguageIndex { get; set; } = -1;

    [ObservableProperty]
    public partial string LocalizedName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LocalizedDescription { get; set; } = string.Empty;

    public bool HasAssistants => _assistants.Count > 0;

    /// <summary>Loads assistants from the folders and shows the first one (Clippit if present).</summary>
    public void Initialize(IEnumerable<string> folders)
    {
        SetAssistants(AssistantCatalog.Scan(folders));
        if (_assistants.Count == 0)
        {
            return;
        }

        var clippit = _assistants.ToList().FindIndex(a => a.Key == "CLIPPIT");
        GalleryIndex = Math.Max(0, clippit);
        UseAssistant();
    }

    public void AddFiles(IEnumerable<string> files)
    {
        var current = GalleryIndex >= 0 ? _assistants[GalleryIndex].Key : null;
        SetAssistants(AssistantCatalog.Merge(_assistants, files));
        var index = _assistants.ToList().FindIndex(a => a.Key == current);
        GalleryIndex = Math.Max(0, index);
        OnGalleryIndexChanged(GalleryIndex);
    }

    [RelayCommand]
    private void Previous() => GalleryIndex = (GalleryIndex - 1 + _assistants.Count) % _assistants.Count;

    [RelayCommand]
    private void Next() => GalleryIndex = (GalleryIndex + 1) % _assistants.Count;

    /// <summary>Makes the assistant shown in the gallery the active one (the gallery's OK).</summary>
    [RelayCommand]
    private void UseAssistant()
    {
        if (GalleryIndex < 0)
        {
            return;
        }

        var definition = _assistants[GalleryIndex];
        _assistant.Load(definition);
        if (!_assistant.IsShown)
        {
            _assistant.Show();
        }

        IsAssistantShown = _assistant.IsShown;
        ActiveIsPreviewOnly = !definition.HasFullCharacter;
        ActiveFiles = string.Join(" + ", new[] { definition.CharacterPath, definition.GalleryPath }.Where(p => p is not null).Select(Path.GetFileName));

        Animations.Clear();
        foreach (var animation in _assistant.Character!.Animations.Select(a => a.Name).Order(StringComparer.OrdinalIgnoreCase))
        {
            Animations.Add(animation);
        }

        RefreshLanguages(_assistant.Character);
        RefreshStrings();
    }

    [RelayCommand]
    private void Animate() => _assistant.Animate();

    [RelayCommand]
    private void PlayAnimation(string? name)
    {
        if (name is null)
        {
            return;
        }

        if (!_assistant.IsShown)
        {
            _assistant.Show(greet: false);
        }

        _assistant.Play(name);
    }

    [RelayCommand]
    private void ShowBalloon(string? kind)
    {
        if (!_assistant.IsShown)
        {
            _assistant.Show(greet: false);
        }

        var strings = _assistant.Strings;
        var content = kind switch
        {
            "answer" => BalloonScenarios.AnswerWizard(),
            "autocomplete" => BalloonScenarios.TipWithAnswerWizard("Word can complete things that you frequently type. Just press enter to accept the completion."),
            "save" => BalloonScenarios.SaveChanges(),
            "letter" => BalloonScenarios.LetterTip(),
            "greeting" when strings?.Greetings.Count > 0 => BalloonScenarios.TipWithAnswerWizard(strings.Greetings[Random.Shared.Next(strings.Greetings.Count)]),
            "reminder" when strings?.Reminders.Count > 0 => BalloonScenarios.Message("Meeting with the Assistant team at 3:00 PM.", strings.Reminders[Random.Shared.Next(strings.Reminders.Count)]),
            "about" when strings is not null => BalloonScenarios.Message(strings.Description, strings.Name),
            _ => null,
        };

        if (content is not null)
        {
            _assistant.ShowBalloon(content);
        }
    }

    [RelayCommand]
    private void CloseBalloon() => _assistant.CloseBalloon();

    partial void OnGalleryIndexChanged(int value)
    {
        if (value < 0 || value >= _assistants.Count)
        {
            return;
        }

        var definition = _assistants[value];
        if (!_galleryCache.TryGetValue(definition.Key, out var gallery))
        {
            gallery = definition.LoadGallery();
            _galleryCache[definition.Key] = gallery;
        }

        GalleryCharacter = gallery;
        GalleryIsPreviewOnly = !definition.HasFullCharacter;
        GalleryPosition = $"{value + 1} of {_assistants.Count}";
        RefreshGalleryText();
        GalleryChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsAssistantShownChanged(bool value)
    {
        if (value == _assistant.IsShown || _assistant.Character is null)
        {
            return;
        }

        if (value)
        {
            _assistant.Show(greet: false);
        }
        else
        {
            _assistant.Hide();
        }
    }

    partial void OnSoundsEnabledChanged(bool value) => _assistant.SoundsEnabled = value;

    partial void OnScaleIndexChanged(int value) => _assistant.Scale = value + 1;

    partial void OnLanguageIndexChanged(int value)
    {
        if (value < 0 || value >= Languages.Count || _updatingLanguage)
        {
            return;
        }

        _assistant.LanguageId = Languages[value].Id;
        RefreshStrings();
        RefreshGalleryText();
    }

    private void SetAssistants(IReadOnlyList<AssistantDefinition> assistants)
    {
        _assistants = assistants;
        OnPropertyChanged(nameof(HasAssistants));
    }

    private void RefreshGalleryText()
    {
        var info = GalleryCharacter?.LocalizedInfo.Count > 0 ? GalleryCharacter.GetLocalizedInfo(_assistant.LanguageId) : null;
        GalleryName = info?.Name ?? (GalleryIndex >= 0 ? _assistants[GalleryIndex].Key : string.Empty);
        GalleryDescription = info?.Description ?? string.Empty;
    }

    private void RefreshLanguages(AcsCharacter character)
    {
        _updatingLanguage = true;
        Languages.Clear();
        foreach (var info in character.LocalizedInfo.OrderBy(l => LanguageName(l.LanguageId), StringComparer.CurrentCulture))
        {
            Languages.Add(new LanguageOption(info.LanguageId, LanguageName(info.LanguageId)));
        }

        var current = character.LocalizedInfo.Count > 0 ? character.GetLocalizedInfo(_assistant.LanguageId).LanguageId : -1;
        LanguageIndex = Languages.ToList().FindIndex(l => l.Id == current);
        _updatingLanguage = false;
    }

    private void RefreshStrings()
    {
        var strings = _assistant.Strings;
        ActiveName = strings?.Name ?? _assistant.Definition?.Key ?? "None";
        LocalizedName = strings?.Name ?? string.Empty;
        LocalizedDescription = strings?.Description ?? string.Empty;

        Greetings.Clear();
        Reminders.Clear();
        foreach (var greeting in strings?.Greetings ?? [])
        {
            Greetings.Add(greeting);
        }

        foreach (var reminder in strings?.Reminders ?? [])
        {
            Reminders.Add(reminder);
        }
    }

    private static string LanguageName(int languageId)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(languageId);
            return $"{culture.DisplayName} ({culture.Name})";
        }
        catch (CultureNotFoundException)
        {
            return $"Language 0x{languageId:X4}";
        }
    }
}
