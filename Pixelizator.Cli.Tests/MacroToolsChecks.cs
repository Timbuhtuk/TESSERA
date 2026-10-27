using System.Drawing.Imaging;
using PixelArtDownscale;
using PixelArtWorkflows;
using Pixelizator.Cli;

internal static class MacroToolsChecks
{
    public static void Run(string directory, Action<string, Action> check)
    {
        check("Connected palette macros preserve every option, alpha and hidden RGB after JSON roundtrip", () =>
        {
            string input = Path.Combine(directory, "macro-palette-input.png");
            using var source = new Bitmap(32, 8, PixelFormat.Format32bppArgb);
            for (int y = 0; y < source.Height; y++)
                for (int x = 0; x < source.Width; x++)
                    source.SetPixel(x, y, Color.FromArgb(y * 32 + x, 20 + x / 8 * 60, 40 + y / 2 * 40, 100));
            source.SetPixel(0, 0, Color.FromArgb(0, 12, 34, 56));
            source.Save(input, ImageFormat.Png);
            byte[] original = File.ReadAllBytes(input);
            var store = new MacroStore(Path.Combine(directory, "macro-palette-library"));
            foreach (var space in new[] { PaletteDistanceSpace.Oklab, PaletteDistanceSpace.CieLab })
                foreach (int tile in new[] { 0, 8 })
                {
                    var options = new ConnectivityPaletteOptions { TargetColors = 3, TileSize = tile,
                        ConnectivityWeight = .25, LocalWeight = .65, DetailWeight = .8, ContextWeight = .15, DistanceSpace = space };
                    var macro = store.Save(new MacroDefinition { Name = $"Palette {space} {tile}", Steps =
                        [new ProcessingStep { OperationId = ProcessingOperations.ConnectedPalette, ConnectedPalette = options }] });
                    string output = Path.Combine(directory, $"macro-palette-{space}-{tile}.png");
                    var run = Invoke(["macro", "apply", input, "--macro", store.PathFor(macro.Id), "-o", output]);
                    Require(run.Code == 0, run.Error);
                    using var expected = ConnectivityPaletteReducer.Process(source, options).Image;
                    using var actual = new Bitmap(output);
                    Equal(expected, actual);
                    for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++)
                        Require(actual.GetPixel(x, y).A == source.GetPixel(x, y).A, "Macro changed alpha");
                    Require(actual.GetPixel(0, 0).ToArgb() == source.GetPixel(0, 0).ToArgb(), "Macro changed hidden RGB");
                }
            Require(File.ReadAllBytes(input).SequenceEqual(original), "Macro changed source PNG");
        });
        check("New macro tools reject missing, mixed and invalid payloads before executing any step", () =>
        {
            ProcessingStep[] invalid =
            [
                new() { OperationId = ProcessingOperations.ConnectedPalette },
                new() { OperationId = ProcessingOperations.ConnectedPalette, ConnectedPalette = new() { TargetColors = 0 } },
                new() { OperationId = ProcessingOperations.ConnectedPalette, ConnectedPalette = new() { TileSize = 3 } },
                new() { OperationId = ProcessingOperations.ConnectedPalette, ConnectedPalette = new() { ContextWeight = double.NaN } },
                new() { OperationId = ProcessingOperations.ConnectedPalette, ConnectedPalette = new() { DistanceSpace = (PaletteDistanceSpace)99 } },
                new() { OperationId = ProcessingOperations.ConnectedPalette, ConnectedPalette = new(), Contours = new() },
                new() { OperationId = ProcessingOperations.ContourHeatmap, Contours = new(), ConnectedPalette = new() },
                new() { OperationId = ProcessingOperations.CleanContours, Contours = new() { MinimumAgreement = double.PositiveInfinity } },
                new() { OperationId = ProcessingOperations.Monochrome, ConnectedPalette = new() }
            ];
            foreach (var step in invalid)
            {
                bool rejected = false;
                try { WorkflowExecutor.Validate(new ProcessingRecipe { Steps =
                    [new ProcessingStep { OperationId = ProcessingOperations.Monochrome }, step] }); }
                catch (WorkflowStepException error) { rejected = error.StepIndex == 2; }
                Require(rejected, "Invalid macro payload was accepted or blamed on the wrong step");
            }
        });
    }

    private static (int Code, string Error) Invoke(string[] args)
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        return (CliApplication.Run(args, output, error), error.ToString());
    }
    private static void Equal(Bitmap expected, Bitmap actual)
    {
        Require(expected.Size == actual.Size, "Macro changed dimensions");
        for (int y = 0; y < expected.Height; y++) for (int x = 0; x < expected.Width; x++)
            Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), $"Macro pixel differs: {x},{y}");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
