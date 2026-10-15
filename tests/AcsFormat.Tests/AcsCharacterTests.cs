namespace AcsFormat.Tests;

public class AcsCharacterTests
{
    [Fact]
    public void LoadsClippitCharacter()
    {
        var clippit = AcsCharacter.Load(TestFiles.Assistant("CLIPPIT.ACS"));

        Assert.Equal(124, clippit.Width);
        Assert.Equal(93, clippit.Height);
        Assert.Equal(43, clippit.Animations.Count);
        Assert.Equal(902, clippit.ImageCount);
        Assert.Equal(15, clippit.SoundCount);
        Assert.Equal(256, clippit.Palette.Count);
        Assert.Equal(0xFFFF00FFu, clippit.Palette[clippit.TransparentIndex]);
        Assert.Equal(["SHOW"], clippit.GetStateAnimations("Showing"));
        Assert.NotNull(clippit.FindAnimation("searching"));
        Assert.Equal("Searching", clippit.FindAnimation("SEARCHING")!.Name);
    }

    [Theory]
    [MemberData(nameof(AllFiles))]
    public void EveryFileDecodesAllImagesSoundsAndFrames(string path)
    {
        var character = AcsCharacter.Load(path);
        var renderer = new AcsFrameRenderer(character);

        Assert.Contains(character.Animations, a => a.Name.Equals("Wave", StringComparison.OrdinalIgnoreCase));
        for (var i = 0; i < character.ImageCount; i++)
        {
            var image = character.GetImage(i);
            Assert.Equal(image.Width * image.Height, image.Pixels.Length);
        }

        for (var i = 0; i < character.SoundCount; i++)
        {
            Assert.Equal("RIFF"u8.ToArray(), character.GetSound(i).WaveData[..4].ToArray());
        }

        foreach (var frame in character.Animations.SelectMany(a => a.Frames))
        {
            Assert.All(frame.Images, image => Assert.InRange(image.ImageIndex, 0, character.ImageCount - 1));
            renderer.Render(frame);
        }
    }

    public static TheoryData<string> AllFiles() => new(TestFiles.AllAssistantFiles());

    [Fact]
    public void ReadsOfficeStringsFromLocalizedInfo()
    {
        var clippit = AcsCharacter.Load(TestFiles.Assistant("CLIPPIT.ACS"));

        var english = clippit.GetLocalizedInfo(0x0409);
        Assert.Equal(0x0009, english.LanguageId);
        Assert.Equal("Clippit", english.Name);
        Assert.StartsWith("Though nothing more than a thin metal wire", english.Description);
        Assert.Equal(3, english.Greetings.Count);
        Assert.Equal("Hey, there. What's the word?", english.Greetings[0]);
        Assert.Equal(["Here's your reminder.", "Don't forget this.", "Consider yourself reminded."], english.Reminders);

        Assert.Equal("Karl Klammer", clippit.GetLocalizedInfo(0x0807).Name); // de-CH falls back to German.
        Assert.Equal("Bindersen", clippit.GetLocalizedInfo(0x0414).Name);
        Assert.Equal("Clippit", clippit.GetLocalizedInfo(0x0436).Name); // Unknown language: English.
    }

    [Fact]
    public void ClippitFrameMatchesOfficeScreenshotExactly()
    {
        var clippit = AcsCharacter.Load(TestFiles.Assistant("CLIPPIT.ACS"));
        var frame = new AcsFrame([new AcsFrameImage(54, 0, 0)], -1, 10, -1, [], []);
        var pixels = new AcsFrameRenderer(clippit).Render(frame);

        var (compared, different) = TestFiles.Compare("what would you like to do.png", pixels, 124, 93, 77, 126);

        Assert.True(compared > 3000);
        Assert.Equal(0, different);
    }

    [Fact]
    public void FirstFrameImageIsDrawnOnTop()
    {
        var dot = AcsCharacter.Load(TestFiles.Assistant("DOT.ACG"));
        var frame = dot.FindAnimation("Wave")!.Frames.First(f => f.Images.Count > 1);
        var top = frame.Images[0];
        var image = dot.GetImage(top.ImageIndex);
        var pixels = new AcsFrameRenderer(dot).Render(frame);

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var index = image[x, y];
                if (index != dot.TransparentIndex && top.X + x < dot.Width && top.Y + y < dot.Height)
                {
                    Assert.Equal(dot.Palette[index], pixels[((top.Y + y) * dot.Width) + top.X + x]);
                }
            }
        }
    }

    [Fact]
    public void RejectsFilesWithoutSignature()
    {
        Assert.Throws<InvalidDataException>(() => AcsCharacter.Load(new byte[64]));
    }

    [Fact]
    public void DecompressorRejectsBadStreams()
    {
        Assert.Throws<InvalidDataException>(() => AcsDecompressor.Decompress([1, 2, 3, 4, 5, 6, 7, 8], 16));
    }
}
