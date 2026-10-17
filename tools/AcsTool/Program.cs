using AcsFormat;
using AcsTool;

// Developer tool: inspect ACS/ACG files and export their frames and sounds.
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
            PngWriter.Write(Path.Combine(folder, $"{animation.Name}_{i:D3}.png"), character.Width, character.Height, pixels);
        }
    }

    for (var i = 0; i < character.SoundCount; i++)
    {
        File.WriteAllBytes(Path.Combine(folder, $"sound_{i:D2}.wav"), character.GetSound(i).WaveData.ToArray());
    }

    Console.WriteLine($"Exported {character.Animations.Sum(a => a.Frames.Count)} frames and {character.SoundCount} sounds to {folder}");
}
