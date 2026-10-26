using System.Drawing.Imaging;
using System.Text.Json;
using PixelArtDownscale;
using Pixelizator.Cli;

internal static class ConnectedPaletteChecks
{
    public static void Run(string directory, Action<string, Action> check)
    {
        check("Connected palette favors a color patch over scattered pixels", () =>
        {
            using var source = new Bitmap(16, 12, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.FromArgb(170, 136, 136, 136));
            for (int y = 3; y < 6; y++) for (int x = 3; x < 6; x++)
                source.SetPixel(x, y, Color.FromArgb(170, 80, 80, 80));
            foreach (var (x, y) in new[] { (10, 1), (12, 3), (14, 5), (10, 8), (13, 10), (11, 6), (8, 10), (14, 1), (8, 2) })
                source.SetPixel(x, y, Color.FromArgb(170, 190, 190, 190));
            source.SetPixel(0, 0, Color.FromArgb(0, 42, 20, 10));
            var report = ConnectivityPaletteReducer.Process(source, new ConnectivityPaletteOptions
            {
                TargetColors = 2, ConnectivityWeight = 1, TileSize = 0,
                LocalWeight = 0, DetailWeight = 0, ContextWeight = 0
            });
            using var image = report.Image;
            Require(report.ColorsBefore == 3 && report.ColorsAfter == 2, "Wrong palette size");
            Require(image.GetPixel(4, 4).R == 80, "Connected color patch was lost");
            Require(image.GetPixel(10, 1).R != 190, "Scattered color was preserved instead");
            Require(report.ChangedPixels == 9, "Wrong replacement count");
            for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++)
                Require(image.GetPixel(x, y).A == source.GetPixel(x, y).A, "Alpha changed");
        });

        check("Connected palette CLI saves target and rejects overwrites and invalid settings", () =>
        {
            string input = Path.Combine(directory, "connected-input.png");
            string result = Path.Combine(directory, "connected-output.png");
            using (var image = new Bitmap(4, 2, PixelFormat.Format32bppArgb))
            {
                for (int x = 0; x < 4; x++) for (int y = 0; y < 2; y++)
                    image.SetPixel(x, y, Color.FromArgb(255, x < 2 ? 0 : 255, 0, 0));
                image.Save(input, ImageFormat.Png);
            }
            byte[] original = File.ReadAllBytes(input);
            var run = Invoke(["connected-palette", input, "-o", result, "--colors", "1",
                "--tile-size", "0", "--space", "lab", "--json"]);
            Require(run.Code == 0, run.Error);
            using (var report = JsonDocument.Parse(run.Output))
                Require(report.RootElement.GetProperty("ColorsAfter").GetInt32() == 1, "Wrong final count");
            using (var resultImage = new Bitmap(result))
                Require(resultImage.Size == new Size(4, 2), "Canvas changed");
            Require(File.ReadAllBytes(input).SequenceEqual(original), "Source changed");
            Require(Invoke(["connected-palette", input, "-o", input]).Code == 2, "Source overwrite accepted");
            Require(Invoke(["connected-palette", input, "-o", result]).Code == 1, "Existing output overwritten");
            Require(Invoke(["connected-palette", input, "--tile-size", "2"]).Code == 2, "Invalid tile accepted");
            Require(Invoke(["connected-palette", input, "--connectivity", "101"]).Code == 2, "Invalid weight accepted");
            Require(Invoke(["connected-palette", "--help"]).Code == 0, "Help unavailable");
        });

        check("Connected palette preserves transparent and partial alpha through PNG", () =>
        {
            string input = Path.Combine(directory, "connected-alpha-input.png");
            string output = Path.Combine(directory, "connected-alpha-output.png");
            using (var image = new Bitmap(4, 1, PixelFormat.Format32bppArgb))
            {
                byte[] alpha = [0, 1, 127, 255];
                for (int x = 0; x < 4; x++) image.SetPixel(x, 0, Color.FromArgb(alpha[x], x % 2 == 0 ? 20 : 220, 40, 60));
                image.Save(input, ImageFormat.Png);
            }
            var run = Invoke(["connected-palette", input, "-o", output, "--colors", "1"]);
            Require(run.Code == 0, run.Error);
            using var reopened = new Bitmap(output);
            byte[] expected = [0, 1, 127, 255];
            for (int x = 0; x < 4; x++) Require(reopened.GetPixel(x, 0).A == expected[x], "Alpha was lost");
        });
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
