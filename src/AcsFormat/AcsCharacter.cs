namespace AcsFormat;

/// <summary>
/// A Microsoft Agent character loaded from an .acs file (or the .acg gallery-preview
/// files shipped with Office, which use the same layout but contain only a preview
/// animation).
/// </summary>
/// <remarks>
/// File layout: a signature followed by four locators pointing at the character info,
/// the animation list, the image list and the audio list. Images and sounds are decoded
/// lazily and cached.
/// </remarks>
public sealed class AcsCharacter
{
    public const uint Signature = 0xABCDABC3;

    private readonly byte[] _data;
    private readonly int[] _imageOffsets;
    private readonly AcsImage?[] _images;
    private readonly AcsLocator[] _soundLocators;
    private readonly AcsSound?[] _sounds;
    private readonly Dictionary<string, AcsAnimation> _animationsByName;

    private AcsCharacter(byte[] data, string? sourcePath)
    {
        _data = data;
        SourcePath = sourcePath;

        var header = new AcsBinaryReader(data);
        if (header.ReadUInt32() != Signature)
        {
            throw new InvalidDataException("Not a Microsoft Agent character file (bad signature).");
        }

        var characterInfo = header.ReadLocator();
        var animationInfo = header.ReadLocator();
        var imageInfo = header.ReadLocator();
        var audioInfo = header.ReadLocator();

        var reader = new AcsBinaryReader(data, (int)characterInfo.Offset);
        MinorVersion = reader.ReadUInt16();
        MajorVersion = reader.ReadUInt16();
        var localizedInfo = reader.ReadLocator();
        Id = reader.ReadGuid();
        Width = reader.ReadUInt16();
        Height = reader.ReadUInt16();
        TransparentIndex = reader.ReadByte();
        Flags = (AcsCharacterFlags)reader.ReadUInt32();
        AnimationSetMajorVersion = reader.ReadUInt16();
        AnimationSetMinorVersion = reader.ReadUInt16();

        if (Flags.HasFlag(AcsCharacterFlags.VoiceOutputEnabled))
        {
            Voice = ReadVoiceInfo(ref reader);
        }

        if (Flags.HasFlag(AcsCharacterFlags.BalloonEnabled))
        {
            Balloon = ReadBalloonInfo(ref reader);
        }

        Palette = ReadPalette(ref reader);

        if (reader.ReadBool())
        {
            // Tray icon: monochrome and color bitmaps; Office characters don't use them.
            reader.ReadBytes(checked((int)reader.ReadUInt32()));
            reader.ReadBytes(checked((int)reader.ReadUInt32()));
        }

        States = ReadStates(ref reader);
        LocalizedInfo = ReadLocalizedInfo(data, localizedInfo);
        Animations = ReadAnimations(data, animationInfo);
        _animationsByName = Animations.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);

        _imageOffsets = ReadImageOffsets(data, imageInfo);
        _images = new AcsImage?[_imageOffsets.Length];
        _soundLocators = ReadSoundLocators(data, audioInfo);
        _sounds = new AcsSound?[_soundLocators.Length];
    }

    public string? SourcePath { get; }

    public int MajorVersion { get; }

    public int MinorVersion { get; }

    public Guid Id { get; }

    /// <summary>Frame width in pixels.</summary>
    public int Width { get; }

    /// <summary>Frame height in pixels.</summary>
    public int Height { get; }

    /// <summary>Palette index treated as fully transparent.</summary>
    public byte TransparentIndex { get; }

    public AcsCharacterFlags Flags { get; }

    public int AnimationSetMajorVersion { get; }

    public int AnimationSetMinorVersion { get; }

    public AcsVoiceInfo? Voice { get; }

    public AcsBalloonInfo? Balloon { get; }

    /// <summary>Palette as opaque 0xAARRGGBB values.</summary>
    public IReadOnlyList<uint> Palette { get; }

    /// <summary>Agent states (e.g. IDLINGLEVEL1, SHOWING) mapped to animation names.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> States { get; }

    public IReadOnlyList<AcsLocalizedInfo> LocalizedInfo { get; }

    public IReadOnlyList<AcsAnimation> Animations { get; }

    public int ImageCount => _imageOffsets.Length;

    public int SoundCount => _soundLocators.Length;

    public static AcsCharacter Load(string path) => new(File.ReadAllBytes(path), path);

    public static AcsCharacter Load(byte[] data) => new(data, null);

    public AcsAnimation? FindAnimation(string name) =>
        _animationsByName.GetValueOrDefault(name);

    /// <summary>Animation names for a state such as "IDLINGLEVEL1", or an empty list.</summary>
    public IReadOnlyList<string> GetStateAnimations(string state) =>
        States.TryGetValue(state, out var names) ? names : [];

    public AcsImage GetImage(int index)
    {
        var image = _images[index];
        if (image is null)
        {
            image = AcsImage.Decode(_data, _imageOffsets[index]);
            _images[index] = image;
        }

        return image;
    }

    public AcsSound GetSound(int index)
    {
        var sound = _sounds[index];
        if (sound is null)
        {
            var locator = _soundLocators[index];
            sound = new AcsSound(_data.AsSpan((int)locator.Offset, (int)locator.Size).ToArray());
            _sounds[index] = sound;
        }

        return sound;
    }

    /// <summary>
    /// Picks the strings for a Windows language id: exact match, then a match on the
    /// primary language, then English, then whatever comes first.
    /// </summary>
    public AcsLocalizedInfo GetLocalizedInfo(int languageId)
    {
        if (LocalizedInfo.Count == 0)
        {
            throw new InvalidOperationException("The character has no localized information.");
        }

        var primary = languageId & 0x3FF;
        return LocalizedInfo.FirstOrDefault(l => l.LanguageId == languageId)
            ?? LocalizedInfo.FirstOrDefault(l => (l.LanguageId & 0x3FF) == primary)
            ?? LocalizedInfo.FirstOrDefault(l => (l.LanguageId & 0x3FF) == 0x09)
            ?? LocalizedInfo[0];
    }

    private static AcsVoiceInfo ReadVoiceInfo(ref AcsBinaryReader reader)
    {
        var engine = reader.ReadGuid();
        var mode = reader.ReadGuid();
        var speed = reader.ReadUInt32();
        var pitch = reader.ReadUInt16();
        if (!reader.ReadBool())
        {
            return new AcsVoiceInfo(engine, mode, speed, pitch, null, null, null, null, null);
        }

        var language = reader.ReadUInt16();
        var dialect = reader.ReadString();
        var gender = reader.ReadUInt16();
        var age = reader.ReadUInt16();
        var style = reader.ReadString();
        return new AcsVoiceInfo(engine, mode, speed, pitch, language, dialect, gender, age, style);
    }

    private static AcsBalloonInfo ReadBalloonInfo(ref AcsBinaryReader reader)
    {
        int lines = reader.ReadByte();
        int charsPerLine = reader.ReadByte();
        var foreground = reader.ReadColorRef();
        var background = reader.ReadColorRef();
        var border = reader.ReadColorRef();
        var fontName = reader.ReadString();
        var fontHeight = reader.ReadInt32();
        var fontWeight = reader.ReadInt32();
        var italic = reader.ReadBool();
        reader.ReadByte(); // Unknown.
        return new AcsBalloonInfo(lines, charsPerLine, foreground, background, border, fontName, fontHeight, fontWeight, italic);
    }

    private static uint[] ReadPalette(ref AcsBinaryReader reader)
    {
        var count = checked((int)reader.ReadUInt32());
        var palette = new uint[count];
        for (var i = 0; i < count; i++)
        {
            // RGBQUAD: blue, green, red, reserved.
            var b = reader.ReadByte();
            var g = reader.ReadByte();
            var r = reader.ReadByte();
            reader.ReadByte();
            palette[i] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
        }

        return palette;
    }

    private static Dictionary<string, IReadOnlyList<string>> ReadStates(ref AcsBinaryReader reader)
    {
        var count = reader.ReadUInt16();
        var states = new Dictionary<string, IReadOnlyList<string>>(count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < count; i++)
        {
            var name = reader.ReadString();
            var animations = new string[reader.ReadUInt16()];
            for (var j = 0; j < animations.Length; j++)
            {
                animations[j] = reader.ReadString();
            }

            states[name] = animations;
        }

        return states;
    }

    private static AcsLocalizedInfo[] ReadLocalizedInfo(byte[] data, AcsLocator locator)
    {
        var reader = new AcsBinaryReader(data, (int)locator.Offset);
        var entries = new AcsLocalizedInfo[reader.ReadUInt16()];
        for (var i = 0; i < entries.Length; i++)
        {
            var language = reader.ReadUInt16();
            var name = reader.ReadString();
            var description = reader.ReadString();
            var extra = reader.ReadString();
            entries[i] = new AcsLocalizedInfo(language, name, description, extra);
        }

        return entries;
    }

    private static AcsAnimation[] ReadAnimations(byte[] data, AcsLocator locator)
    {
        var list = new AcsBinaryReader(data, (int)locator.Offset);
        var animations = new AcsAnimation[list.ReadUInt32()];
        for (var i = 0; i < animations.Length; i++)
        {
            // The list holds the display name; the animation block repeats it in upper case.
            var name = list.ReadString();
            var animationLocator = list.ReadLocator();
            animations[i] = ReadAnimation(data, (int)animationLocator.Offset, name);
        }

        return animations;
    }

    private static AcsAnimation ReadAnimation(byte[] data, int offset, string name)
    {
        var reader = new AcsBinaryReader(data, offset);
        reader.ReadString();
        var transition = (AcsTransitionType)reader.ReadByte();
        var returnAnimation = reader.ReadString();
        var frames = new AcsFrame[reader.ReadUInt16()];

        for (var f = 0; f < frames.Length; f++)
        {
            var images = new AcsFrameImage[reader.ReadUInt16()];
            for (var i = 0; i < images.Length; i++)
            {
                images[i] = new AcsFrameImage(checked((int)reader.ReadUInt32()), reader.ReadInt16(), reader.ReadInt16());
            }

            var sound = reader.ReadUInt16();
            var duration = reader.ReadUInt16();
            var exitFrame = reader.ReadInt16();

            var branches = new AcsBranch[reader.ReadByte()];
            for (var b = 0; b < branches.Length; b++)
            {
                branches[b] = new AcsBranch(reader.ReadUInt16(), reader.ReadUInt16());
            }

            var overlays = new AcsOverlay[reader.ReadByte()];
            for (var o = 0; o < overlays.Length; o++)
            {
                var shape = (AcsMouthShape)reader.ReadByte();
                var replaces = reader.ReadBool();
                var imageIndex = reader.ReadUInt16();
                reader.ReadByte(); // Unknown.
                var hasRegion = reader.ReadBool();
                var x = reader.ReadInt16();
                var y = reader.ReadInt16();
                var width = reader.ReadUInt16();
                var height = reader.ReadUInt16();
                if (hasRegion)
                {
                    reader.ReadBytes(checked((int)reader.ReadUInt32()));
                }

                overlays[o] = new AcsOverlay(shape, replaces, imageIndex, x, y, width, height);
            }

            frames[f] = new AcsFrame(images, sound == 0xFFFF ? -1 : sound, duration, exitFrame, branches, overlays);
        }

        return new AcsAnimation(name, transition, returnAnimation, frames);
    }

    private static int[] ReadImageOffsets(byte[] data, AcsLocator locator)
    {
        var reader = new AcsBinaryReader(data, (int)locator.Offset);
        var offsets = new int[reader.ReadUInt32()];
        for (var i = 0; i < offsets.Length; i++)
        {
            offsets[i] = (int)reader.ReadLocator().Offset;
            reader.ReadUInt32(); // Checksum.
        }

        return offsets;
    }

    private static AcsLocator[] ReadSoundLocators(byte[] data, AcsLocator locator)
    {
        var reader = new AcsBinaryReader(data, (int)locator.Offset);
        var locators = new AcsLocator[reader.ReadUInt32()];
        for (var i = 0; i < locators.Length; i++)
        {
            locators[i] = reader.ReadLocator();
            reader.ReadUInt32(); // Checksum.
        }

        return locators;
    }
}
