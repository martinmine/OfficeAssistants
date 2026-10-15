using AcsFormat;

namespace OfficeAssistant.Core;

/// <summary>
/// One assistant as Office installs it: a full character file (.acs) and/or a gallery
/// preview file (.acg) that only holds the animation shown in the Assistant gallery.
/// </summary>
public sealed class AssistantDefinition
{
    internal AssistantDefinition(string key)
    {
        Key = key;
    }

    /// <summary>File name without extension, e.g. CLIPPIT.</summary>
    public string Key { get; }

    public string? CharacterPath { get; internal set; }

    public string? GalleryPath { get; internal set; }

    /// <summary>True when the full character (all animations) is available.</summary>
    public bool HasFullCharacter => CharacterPath is not null;

    /// <summary>Loads the file the gallery shows: the .acg if present, else the .acs.</summary>
    public AcsCharacter LoadGallery() => AcsCharacter.Load(GalleryPath ?? CharacterPath!);

    /// <summary>Loads the file used on screen: the .acs if present, else the gallery preview.</summary>
    public AcsCharacter LoadCharacter() => AcsCharacter.Load(CharacterPath ?? GalleryPath!);

    public override string ToString() => Key;
}

public static class AssistantCatalog
{
    /// <summary>Finds .acs/.acg files in the folders and pairs them by file name.</summary>
    public static IReadOnlyList<AssistantDefinition> Scan(IEnumerable<string> folders)
    {
        var byKey = new Dictionary<string, AssistantDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(folder))
            {
                Add(byKey, path);
            }
        }

        return byKey.Values.OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Adds individual files (for example picked by the user) to an existing list.</summary>
    public static IReadOnlyList<AssistantDefinition> Merge(IEnumerable<AssistantDefinition> existing, IEnumerable<string> files)
    {
        var byKey = existing.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            Add(byKey, file);
        }

        return byKey.Values.OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void Add(Dictionary<string, AssistantDefinition> byKey, string path)
    {
        var extension = Path.GetExtension(path);
        var isCharacter = extension.Equals(".acs", StringComparison.OrdinalIgnoreCase);
        var isGallery = extension.Equals(".acg", StringComparison.OrdinalIgnoreCase);
        if (!isCharacter && !isGallery)
        {
            return;
        }

        var key = Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
        if (!byKey.TryGetValue(key, out var definition))
        {
            definition = new AssistantDefinition(key);
            byKey[key] = definition;
        }

        if (isCharacter)
        {
            definition.CharacterPath = path;
        }
        else
        {
            definition.GalleryPath = path;
        }
    }
}
