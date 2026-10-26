using System.Drawing.Imaging;
using System.Text.Json;
using PixelArtDownscale;
using Pixelizator.Cli;

internal static class DarkContourChecks
{
    public static void Run(string directory, Action<string, Action> check)
    {
        check("Dark contour grid restores known blocks at multiple sizes and offsets", () =>
        {
            foreach (int size in new[] { 2, 3, 4 })
            foreach (int offset in new[] { 0, 1 })
            {
                using var clean = new Bitmap(20, 20, PixelFormat.Format32bppArgb);
                for (int y = 0; y < clean.Height; y++)
                    for (int x = 0; x < clean.Width; x++)
                        clean.SetPixel(x, y, ((x + offset) / size + (y + offset) / size) % 2 == 0
                            ? Color.FromArgb(127, 180, 200, 220) : Color.FromArgb(127, 210, 150, 100));
                using var noisy = (Bitmap)clean.Clone();
                noisy.SetPixel(9, 9, Color.FromArgb(127, 2, 3, 5));
                var result = GridSingletonNoiseCleaner.Clean(noisy, size);
                using var output = result.Image;
                Require(result.ChangedPixels == 1, "Expected exactly the inserted singleton");
                Equal(clean, output);
            }
        });

        check("Dark contour grid preserves all alpha values and transparent hidden RGB", () =>
        {
            for (int alpha = 0; alpha <= 255; alpha++)
            {
                using var source = Solid(5, 5, Color.FromArgb(255, 200, 210, 220));
                source.SetPixel(2, 2, Color.FromArgb(alpha, 3, 4, 5));
                var result = GridSingletonNoiseCleaner.Clean(source, 2);
                using var output = result.Image;
                Require(output.GetPixel(2, 2).A == alpha, "Grid changed opacity at " + alpha);
                if (alpha < 255) Equal(source, output);
                else Require(result.ChangedPixels == 1, "Opaque singleton was not corrected");
            }
            using var hidden = Solid(5, 5, Color.FromArgb(0, 12, 34, 56));
            hidden.SetPixel(2, 2, Color.FromArgb(0, 78, 90, 123));
            var hiddenResult = GridSingletonNoiseCleaner.Clean(hidden, 2);
            using var hiddenOutput = hiddenResult.Image;
            Equal(hidden, hiddenOutput);
        });

        check("Dark contour grid rejects ambiguous colors and retains borders", () =>
        {
            using var source = Solid(5, 5, Color.White);
            source.SetPixel(3, 3, Color.Red);
            source.SetPixel(3, 2, Color.Red);
            source.SetPixel(2, 3, Color.Red);
            source.SetPixel(2, 2, Color.Black);
            source.SetPixel(0, 2, Color.Blue);
            var result = GridSingletonNoiseCleaner.Clean(source, 2);
            using var output = result.Image;
            Require(output.GetPixel(2, 2).ToArgb() == Color.Black.ToArgb(), "Ambiguous color was guessed");
            Require(output.GetPixel(0, 2).ToArgb() == Color.Blue.ToArgb(), "Border pixel changed");
        });

        check("Dark contour CLI saves exact grid PNG, JSON and audit without changing source", () =>
        {
            string sourcePath = Path.Combine(directory, "dark-grid.png");
            string outputPath = Path.Combine(directory, "dark-grid-result.png");
            string auditPath = Path.Combine(directory, "dark-grid.csv");
            using (var source = Solid(7, 7, Color.FromArgb(128, 180, 200, 220)))
            {
                source.SetPixel(3, 3, Color.FromArgb(128, 2, 3, 5));
                source.Save(sourcePath, ImageFormat.Png);
            }
            byte[] original = File.ReadAllBytes(sourcePath);
            var run = Invoke(["clean-dark-contours", sourcePath, "-o", outputPath,
                "--grid-cell-size", "2", "--passes", "2", "--candidate-audit", auditPath, "--json"]);
            Require(run.Code == 0, run.Error);
            using var report = JsonDocument.Parse(run.Output);
            Require(report.RootElement.GetProperty("changedPixels").GetInt32() == 1, "Wrong change count");
            Require(report.RootElement.GetProperty("process").GetString() == "clean-dark-contours-grid", "Wrong mode");
            using var expected = Solid(7, 7, Color.FromArgb(128, 180, 200, 220));
            using var output = new Bitmap(outputPath);
            Equal(expected, output);
            Require(File.ReadAllBytes(sourcePath).SequenceEqual(original), "Source was rewritten");
            Require(File.ReadAllText(auditPath).Contains("accepted-grid-singleton"), "Missing audit decision");
        });

        check("Dark contour CLI rejects aliases, overwrites and invalid thresholds", () =>
        {
            string source = Path.Combine(directory, "dark-validation.png");
            using (var image = Solid(5, 5, Color.White)) image.Save(source, ImageFormat.Png);
            byte[] original = File.ReadAllBytes(source);
            string target = Path.Combine(directory, "dark-invalid.png");
            foreach (string[] option in new[]
            {
                new[] { "--grid-cell-size", "1" }, new[] { "--grid-cell-size", "257" },
                new[] { "--grid-cell-size", "6" }, new[] { "--grid-color-tolerance", "10" },
                new[] { "--grid-color-tolerance", "NaN" }, new[] { "--dark-lightness", "NaN" },
                new[] { "--dark-lightness", "Infinity" }, new[] { "--passes", "0" }
            })
            {
                var args = new[] { "clean-dark-contours", source, "-o", target }.Concat(option).ToArray();
                Require(Invoke(args).Code == 2, "Invalid option accepted: " + string.Join(' ', option));
                Require(!File.Exists(target), "Invalid arguments created PNG");
            }
            Require(Invoke(["clean-dark-contours", source, "-o", source, "--overwrite"]).Code == 2, "Source overwrite accepted");
            Require(Invoke(["clean-dark-contours", source, "-o", target, "--candidate-audit", source, "--overwrite"]).Code == 2, "Audit overwrote source");
            Require(Invoke(["clean-dark-contours", source, "-o", target, "--candidate-audit", target]).Code == 2, "Audit/PNG alias accepted");
            File.WriteAllText(target, "preserve this file");
            Require(Invoke(["clean-dark-contours", source, "-o", target]).Code == 1, "Overwrite protection missing");
            Require(File.ReadAllText(target) == "preserve this file", "Existing output changed");
            Require(File.ReadAllBytes(source).SequenceEqual(original), "Validation changed source");
        });

        check("Dark contour CLI preserves default-mode alpha, hidden RGB and tiny images", () =>
        {
            using var source = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    source.SetPixel(x, y, Color.FromArgb(y * 16 + x, 120, 150, 180));
            string path = Path.Combine(directory, "dark-alpha.png");
            string target = Path.Combine(directory, "dark-alpha-output.png");
            source.Save(path, ImageFormat.Png);
            var run = Invoke(["clean-dark-contours", path, "-o", target, "--json"]);
            Require(run.Code == 0, run.Error);
            using (var output = new Bitmap(target)) Equal(source, output);
            foreach (int size in new[] { 1, 2 })
            {
                using var tiny = Solid(size, 1, Color.Black);
                tiny.Save(path, ImageFormat.Png);
                Require(Invoke(["clean-dark-contours", path, "-o", target, "--overwrite"]).Code == 0, "Tiny image failed");
            }
            var help = Invoke(["clean-dark-contours", "--help"]);
            Require(help.Code == 0 && help.Output.Contains("--grid-cell-size") && help.Output.Contains("--passes"), "Command help missing");
        });
    }

    private static Bitmap Solid(int width, int height, Color color)
    {
        var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) image.SetPixel(x, y, color);
        return image;
    }

    private static void Equal(Bitmap expected, Bitmap actual)
    {
        Require(expected.Size == actual.Size, "Image dimensions changed");
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), $"Pixel differs at {x},{y}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static (int Code, string Output, string Error) Invoke(string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int code = CliApplication.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }
}
