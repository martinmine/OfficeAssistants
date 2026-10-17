using AcsFormat;
using OfficeAssistant.Core.Rendering;
using StbImageWriteSharp;

// Developer tool: inspect ACS/ACG files, export their frames and sounds, and render
// balloons to PNG for pixel comparisons against the reference screenshots.
if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

switch (args[0].ToLowerInvariant())
{
    case "info" when args.Length >= 2:
        Info(AcsCharacter.Load(args[1]));
        return 0;

    case "export" when args.Length >= 3:
        Export(AcsCharacter.Load(args[1]), args[2]);
        return 0;

    case "balloon" when args.Length >= 3:
        RenderBalloon(args[1], args[2], args.Length >= 4 ? int.Parse(args[3]) : 90, args.Length >= 5 && args[4] == "top");
        return 0;

    default:
        PrintUsage();
        return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        Usage:
          AcsTool info <file.acs|file.acg>
          AcsTool export <file.acs|file.acg> <output-folder>
          AcsTool balloon <answer|autocomplete|results|save|letter> <out.png> [tailX] [top]
        """);
}

static void Info(AcsCharacter character)
{
    Console.WriteLine($"Version {character.MajorVersion}.{character.MinorVersion}, {character.Width}x{character.Height}, flags {character.Flags}");
    Console.WriteLine($"Images {character.ImageCount}, sounds {character.SoundCount}, transparent index {character.TransparentIndex}");
    if (character.Balloon is { } balloon)
    {
        Console.WriteLine($"Balloon: {balloon.FontName} {balloon.FontHeight}, {balloon.Lines} lines x {balloon.CharactersPerLine}");
    }

    Console.WriteLine();
    Console.WriteLine("States:");
    foreach (var (state, animations) in character.States)
    {
        Console.WriteLine($"  {state}: {string.Join(", ", animations)}");
    }

    Console.WriteLine();
    Console.WriteLine("Animations:");
    foreach (var animation in character.Animations)
    {
        var sounds = animation.Frames.Count(f => f.HasSound);
        var duration = animation.Frames.Sum(f => f.DurationMilliseconds);
        Console.WriteLine($"  {animation.Name,-20} {animation.Frames.Count,3} frames  {duration,6} ms  {sounds} sounds  {animation.TransitionType}");
    }

    Console.WriteLine();
    Console.WriteLine("Localized info:");
    foreach (var info in character.LocalizedInfo)
    {
        Console.WriteLine($"  0x{info.LanguageId:X4} {info.Name}: {info.Description}");
        foreach (var greeting in info.Greetings)
        {
            Console.WriteLine($"         greeting: {greeting}");
        }

        foreach (var reminder in info.Reminders)
        {
            Console.WriteLine($"         reminder: {reminder}");
        }
    }
}

static void Export(AcsCharacter character, string folder)
{
    Directory.CreateDirectory(folder);
    var renderer = new AcsFrameRenderer(character);
    foreach (var animation in character.Animations)
    {
        for (var i = 0; i < animation.Frames.Count; i++)
        {
            var pixels = renderer.Render(animation.Frames[i]);
            SavePng(Path.Combine(folder, $"{animation.Name}_{i:D3}.png"), character.Width, character.Height, pixels);
        }
    }

    for (var i = 0; i < character.SoundCount; i++)
    {
        File.WriteAllBytes(Path.Combine(folder, $"sound_{i:D2}.wav"), character.GetSound(i).WaveData.ToArray());
    }

    Console.WriteLine($"Exported {character.Animations.Sum(a => a.Frames.Count)} frames and {character.SoundCount} sounds to {folder}");
}

static void RenderBalloon(string scenario, string output, int tailX, bool tailOnTop)
{
    var state = new BalloonState();
    BalloonContent content;
    switch (scenario)
    {
        case "answer":
            content = BalloonScenarios.AnswerWizard();
            state.TextBoxText = BalloonScenarios.QuestionPlaceholder;
            break;
        case "autocomplete":
            content = BalloonScenarios.TipWithAnswerWizard("Word can complete things that you frequently type. Just press enter to accept the completion.");
            state.TextBoxText = BalloonScenarios.QuestionPlaceholder;
            state.SelectAll();
            break;
        case "results":
            content = BalloonScenarios.SearchResults(
                [("1", "Print merged documents"), ("2", "Cancel printing"), ("3", "Print a document to a file"), ("4", "Print a document"), ("5", "WEB: Printing troubleshooter")],
                hasMore: true);
            state.TextBoxText = "Print";
            state.SelectAll();
            break;
        case "save":
            content = BalloonScenarios.SaveChanges();
            break;
        case "letter":
            content = BalloonScenarios.LetterTip();
            break;
        default:
            throw new ArgumentException($"Unknown scenario '{scenario}'.");
    }

    using var renderer = new BalloonRenderer();
    var layout = renderer.Measure(content, state);
    var bitmap = renderer.Render(layout, state, tailOnTop ? BalloonTailSide.Top : BalloonTailSide.Bottom, tailX);
    SavePng(output, bitmap.Width, bitmap.Height, bitmap.Pixels);
    Console.WriteLine($"{scenario}: box {layout.Width}x{layout.Height}, bitmap {bitmap.Width}x{bitmap.Height}");
}

/// <summary>Saves premultiplied 0xAARRGGBB pixels with 1-bit alpha (so also straight ARGB) as a PNG.</summary>
static void SavePng(string path, int width, int height, ReadOnlySpan<uint> pixels)
{
    var rgba = new byte[width * height * 4];
    for (var i = 0; i < width * height; i++)
    {
        var p = pixels[i];
        rgba[(i * 4) + 0] = (byte)(p >> 16);
        rgba[(i * 4) + 1] = (byte)(p >> 8);
        rgba[(i * 4) + 2] = (byte)p;
        rgba[(i * 4) + 3] = (byte)(p >> 24);
    }

    using var stream = File.Create(path);
    new ImageWriter().WritePng(rgba, width, height, ColorComponents.RedGreenBlueAlpha, stream);
}
