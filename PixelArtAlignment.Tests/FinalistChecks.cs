using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using PixelArtDownscale;

namespace PixelArtAlignment.Tests;

internal static class FinalistChecks
{
    public static void Run(string labRoot)
    {
        string root = Path.GetFullPath(labRoot);
        using var preview = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reports/gallery/previews/t3-p44_p5-off_50-50/manifest.json")));
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "registry/data.json")));
        string outputRoot = Path.GetFullPath(Path.Combine("artifacts", "finalists-verification"));
        Directory.CreateDirectory(outputRoot);
        int comparisons = 0;
        foreach (var file in preview.RootElement.GetProperty("files").EnumerateArray())
        {
            string name = file.GetProperty("subject").GetString()!;
            var sourceRecord = data.RootElement.GetProperty("originals").EnumerateArray().Single(e => Path.GetFileName(e.GetProperty("path").GetString()) == name);
            string sourcePath = Path.Combine(root, sourceRecord.GetProperty("path").GetString()!);
            CheckHash(sourcePath, sourceRecord.GetProperty("sha256").GetString()!);
            using var source = new Bitmap(sourcePath);
            Require(source.Size == new Size(1254, 1254), "Wrong source dimensions: " + name);
            foreach (var (mode, record, label) in new[]
            {
                (FinalistMode.T3P44, file.GetProperty("t3"), "t3"),
                (FinalistMode.P5Off, file.GetProperty("p5"), "p5"),
                (FinalistMode.HalfAndHalf, file, "mix")
            })
            {
                string referencePath = Path.Combine(root, record.GetProperty(mode == FinalistMode.HalfAndHalf ? "output" : "path").GetString()!);
                CheckHash(referencePath, record.GetProperty(mode == FinalistMode.HalfAndHalf ? "output_sha256" : "sha256").GetString()!);
                using var expected = new Bitmap(referencePath);
                using var actual = FinalistReducer.Reduce(source, mode);
                Equal(expected, actual, name + "/" + label);
                string exported = Path.Combine(outputRoot, Path.GetFileNameWithoutExtension(name) + "-" + label + ".png");
                actual.Save(exported, ImageFormat.Png);
                using var reopened = new Bitmap(exported);
                Equal(actual, reopened, name + "/" + label + " reopen");
                comparisons++;
            }
            Console.WriteLine("  Exact RGBA and PNG reopen: " + name);
        }
        Require(comparisons == 15, "Expected five subjects and three modes");
        foreach (var size in new[] { new Size(1, 1), new Size(9, 9), new Size(13, 21), new Size(20, 20) })
        {
            using var sample = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            for (int y = 0; y < size.Height; y++)
            for (int x = 0; x < size.Width; x++)
                sample.SetPixel(x, y, Color.FromArgb((x * 37 + y * 19) % 256, x * 11 % 256, y * 13 % 256, 173));
            foreach (FinalistMode mode in Enum.GetValues<FinalistMode>())
            {
                using var actual = FinalistReducer.Reduce(sample, mode);
                Require(actual.Size == new Size((size.Width + 7) / 8, (size.Height + 7) / 8), "Wrong general image size");
            }
        }
        Require(FinalistReducer.Blend((100, 50, 25, 0), (10, 20, 30, 255)) == (10, 20, 30, 128), "Transparent input contaminated mixture");
        Require(FinalistReducer.Blend((100, 50, 25, 0), (10, 20, 30, 0)) == (0, 0, 0, 0), "Zero alpha retained RGB");
        Console.WriteLine($"PASS finalists: {comparisons} exact RGBA comparisons, verified reference/input SHA-256, PNG reopen, general sizes and alpha cases. Exports: {outputRoot}");
    }

    private static void CheckHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        string actual = Convert.ToHexString(SHA256.HashData(stream));
        Require(actual.Equals(expected, StringComparison.OrdinalIgnoreCase), "SHA-256 mismatch: " + path);
    }

    private static void Equal(Bitmap expected, Bitmap actual, string name)
    {
        Require(expected.Size == actual.Size, "Dimensions differ: " + name);
        for (int y = 0; y < expected.Height; y++)
        for (int x = 0; x < expected.Width; x++)
            Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), $"RGBA differs: {name} at {x},{y}");
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidDataException(reason);
    }
}
