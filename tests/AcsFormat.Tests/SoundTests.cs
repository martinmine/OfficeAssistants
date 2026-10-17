using System.Text;
using OfficeAssistant.Core.Animation;

namespace AcsFormat.Tests;

public class SoundTests
{
    [Fact]
    public void EverySoundOfEveryAssistantIsAWaveFileWithFormatAndData()
    {
        var sounds = 0;
        foreach (var path in TestFiles.AllAssistantFiles())
        {
            var character = AcsCharacter.Load(path);
            for (var i = 0; i < character.SoundCount; i++)
            {
                var wave = character.GetSound(i).WaveData.Span;
                Assert.True(WaveFile.TryParse(wave, out var format, out var data), $"{Path.GetFileName(path)} sound {i}");
                Assert.True(wave[format].Length >= 16);
                Assert.True(wave[data].Length > 0);
                sounds++;
            }
        }

        Assert.True(sounds > 100);
    }

    [Fact]
    public void ChunksAfterTheFormatAreSkippedIncludingPadding()
    {
        var wave = new List<byte>();
        void Add(string text) => wave.AddRange(Encoding.ASCII.GetBytes(text));
        void Int(int value) => wave.AddRange(BitConverter.GetBytes(value));

        Add("RIFF");
        Int(0);
        Add("WAVE");
        Add("fmt ");
        Int(16);
        wave.AddRange(new byte[16]);
        Add("LIST");
        Int(3);
        wave.AddRange(new byte[4]); // 3 bytes plus 1 byte of padding.
        Add("data");
        Int(100); // Claims more than the file holds.
        wave.AddRange(new byte[10]);

        Assert.True(WaveFile.TryParse(wave.ToArray(), out var format, out var data));
        Assert.Equal(20..36, format);
        Assert.Equal(56..66, data);
        Assert.False(WaveFile.TryParse(Encoding.ASCII.GetBytes("RIFF....AVI "), out _, out _));
    }
}
