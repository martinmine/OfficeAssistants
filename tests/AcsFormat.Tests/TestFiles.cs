using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AcsFormat.Tests;

/// <summary>Locates the repository's Assistants and Screenshots folders.</summary>
internal static class TestFiles
{
    private static readonly Lazy<string> Root = new(() =>
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Assistants")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find the Assistants folder.");
    });

    public static string Assistant(string fileName) => Path.Combine(Root.Value, "Assistants", fileName);

    public static string Screenshot(string fileName) => Path.Combine(Root.Value, "Screenshots", fileName);

    public static IEnumerable<string> AllAssistantFiles() =>
        Directory.EnumerateFiles(Path.Combine(Root.Value, "Assistants"))
            .Where(f => f.EndsWith(".acs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".acg", StringComparison.OrdinalIgnoreCase));

    /// <summary>Loads an image as 0x00RRGGBB values (alpha dropped).</summary>
    public static (int Width, int Height, uint[] Pixels) LoadRgb(string path)
    {
        using var bitmap = new Bitmap(path);
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new uint[bitmap.Width * bitmap.Height];
            for (var y = 0; y < bitmap.Height; y++)
            {
                var row = new int[bitmap.Width];
                Marshal.Copy(data.Scan0 + (y * data.Stride), row, 0, bitmap.Width);
                for (var x = 0; x < bitmap.Width; x++)
                {
                    pixels[(y * bitmap.Width) + x] = (uint)row[x] & 0x00FFFFFF;
                }
            }

            return (bitmap.Width, bitmap.Height, pixels);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>
    /// Counts opaque pixels of <paramref name="pixels"/> that differ from the screenshot
    /// when placed at (<paramref name="left"/>, <paramref name="top"/>). Parts outside the
    /// screenshot are ignored; returns (compared, different).
    /// </summary>
    public static (int Compared, int Different) Compare(string screenshot, uint[] pixels, int width, int height, int left, int top)
    {
        var shot = LoadRgb(Screenshot(screenshot));
        var compared = 0;
        var different = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = pixels[(y * width) + x];
                var sx = left + x;
                var sy = top + y;
                if ((p >> 24) == 0 || sx < 0 || sy < 0 || sx >= shot.Width || sy >= shot.Height)
                {
                    continue;
                }

                compared++;
                if ((p & 0x00FFFFFF) != shot.Pixels[(sy * shot.Width) + sx])
                {
                    different++;
                }
            }
        }

        return (compared, different);
    }
}
