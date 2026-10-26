using System.Drawing.Imaging;
using System.Text.Json;
using PixelArtDownscale;
using Pixelizator.Cli;

internal static class StructureProbeChecks
{
    public static void Run(string directory, Action<string, Action> check)
    {
        check("Structure probe distinguishes a speckle, an eye, a diagonal line and alpha edge", () =>
        {
            using var source = CreateFixture();
            var options = new StructureProbeOptions
            {
                SimilarColorDistance = 0,
                MaximumComponentSize = 8,
                MaximumReplacementDistance = 100,
                DistinctDetailDistance = 50,
                MinimumConfidence = 0.5
            };
            var analysis = StructureProbe.Process(source, options);
            using var result = analysis.Image;
            using var map = analysis.Heatmap;
            Require(result.Size == source.Size && map.Size == source.Size, "Canvas size changed");
            Require(result.GetPixel(5, 5).ToArgb() == Color.FromArgb(127, 180, 180, 180).ToArgb(),
                "The supported speckle was not corrected");
            Require(result.GetPixel(9, 2).ToArgb() == source.GetPixel(9, 2).ToArgb(), "Distinct eye changed");
            for (int q = 0; q < 4; q++)
                Require(result.GetPixel(2 + q, 8 + q).ToArgb() == source.GetPixel(2 + q, 8 + q).ToArgb(),
                    "Diagonal line changed");
            Require(result.GetPixel(1, 1).ToArgb() == source.GetPixel(1, 1).ToArgb(), "Alpha edge changed");
            for (int y = 0; y < source.Height; y++)
                for (int x = 0; x < source.Width; x++)
                    Require(result.GetPixel(x, y).A == source.GetPixel(x, y).A &&
                        map.GetPixel(x, y).A == source.GetPixel(x, y).A, "Alpha changed");
            Require(analysis.ChangedPixels == 1, "Unexpected changed pixel count");
        });

        check("Structure probe CLI saves result and map and rejects source overwrite", () =>
        {
            string input = Path.Combine(directory, "structure-input.png");
            string result = Path.Combine(directory, "structure-result.png");
            string map = Path.Combine(directory, "structure-map.png");
            using (var fixture = CreateFixture()) fixture.Save(input, ImageFormat.Png);
            byte[] original = File.ReadAllBytes(input);
            var run = Invoke(["structure-probe", input, "-o", result, "--map-output", map,
                "--similar-color", "0", "--max-replacement-distance", "100",
                "--detail-distance", "50", "--minimum-confidence", "50", "--json"]);
            Require(run.Code == 0, run.Error);
            using (var report = JsonDocument.Parse(run.Output))
            {
                Require(report.RootElement.GetProperty("process").GetString() == "structure-probe", "Wrong process");
                Require(report.RootElement.GetProperty("ChangedPixels").GetInt32() == 1, "Wrong changed pixel count");
            }
            using (var image = new Bitmap(result))
            using (var heat = new Bitmap(map))
            {
                Require(image.GetPixel(5, 5).R == 180, "CLI result missed the speckle");
                Require(heat.GetPixel(5, 5).R == 255, "Map missed the candidate");
            }
            Require(original.SequenceEqual(File.ReadAllBytes(input)), "Source was rewritten");
            Require(Invoke(["structure-probe", input, "-o", input]).Code == 2, "Source overwrite was accepted");
            Require(Invoke(["structure-probe", input, "-o", result]).Code == 1, "Existing result was overwritten");
            Require(Invoke(["structure-probe", input, "--similar-color", "101"]).Code == 2,
                "Invalid similarity was accepted");
        });
    }

    private static Bitmap CreateFixture()
    {
        var source = new Bitmap(14, 14, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.FromArgb(127, 180, 180, 180));
        source.SetPixel(5, 5, Color.FromArgb(127, 100, 100, 100));
        source.SetPixel(9, 2, Color.FromArgb(127, 0, 0, 0));
        for (int q = 0; q < 4; q++) source.SetPixel(2 + q, 8 + q, Color.FromArgb(127, 100, 100, 100));
        source.SetPixel(0, 0, Color.Transparent);
        source.SetPixel(1, 1, Color.FromArgb(127, 100, 100, 100));
        return source;
    }

    private static (int Code, string Output, string Error) Invoke(string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        return (CliApplication.Run(args, output, error), output.ToString(), error.ToString());
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
