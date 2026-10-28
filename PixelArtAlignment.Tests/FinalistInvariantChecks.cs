using System.Drawing.Imaging;
using PixelArtDownscale;

namespace PixelArtAlignment.Tests;

internal static class FinalistInvariantChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("TCR factors 2..64 preserve edges, alpha and legacy macro defaults", () =>
        {
            using var source = new Bitmap(19, 13, PixelFormat.Format32bppArgb);
            for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++)
                source.SetPixel(x, y, Color.FromArgb(127, 73, 129, 211));
            foreach (int factor in Enumerable.Range(2, 63))
            foreach (FinalistMode mode in Enum.GetValues<FinalistMode>())
            {
                using var output = FinalistReducer.Reduce(source, mode, factor);
                Require(output.Size == new Size((source.Width + factor - 1) / factor, (source.Height + factor - 1) / factor), "Variable factor dimensions");
                for (int y = 0; y < output.Height; y++) for (int x = 0; x < output.Width; x++)
                    Require(output.GetPixel(x, y).ToArgb() == Color.FromArgb(127, 73, 129, 211).ToArgb(), "Variable factor lost an edge/alpha");
            }
            var recipe = System.Text.Json.JsonSerializer.Deserialize<PixelArtWorkflows.ProcessingRecipe>(
                "{\"steps\":[{\"operationId\":\"image.finalist.reduce\",\"finalistMode\":\"T3P44\"}]}", PixelArtWorkflows.WorkflowJson.Options)!;
            using var legacy = new PixelArtWorkflows.WorkflowExecutor().Execute(source, recipe);
            using var expected = FinalistReducer.Reduce(source, FinalistMode.T3P44, 8);
            Require(legacy.Size == expected.Size && legacy.GetPixel(0, 0).ToArgb() == expected.GetPixel(0, 0).ToArgb(), "Legacy macro factor changed");
            foreach (int invalid in new[] { int.MinValue, 0, 1, 65, int.MaxValue })
            {
                bool rejected = false;
                try { using var result = FinalistReducer.Reduce(source, FinalistMode.T3P44, invalid); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Require(rejected, "Invalid reduction factor accepted");
            }
        });
        check("TCR reducers preserve all uniform alpha levels and ignore fully transparent RGB", () =>
        {
            using var source = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
            foreach (int alpha in Enumerable.Range(0, 256))
            {
                for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                    source.SetPixel(x, y, Color.FromArgb(alpha, 73, 129, 211));
                foreach (FinalistMode mode in Enum.GetValues<FinalistMode>())
                {
                    using var output = FinalistReducer.Reduce(source, mode);
                    Color expected = alpha == 0 ? Color.FromArgb(0, 0, 0, 0) : Color.FromArgb(alpha, 73, 129, 211);
                    Require(output.GetPixel(0, 0).ToArgb() == expected.ToArgb(), $"Uniform alpha {alpha}/{mode}");
                }
            }
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                source.SetPixel(x, y, Color.FromArgb(x < 4 ? 0 : 255, x < 4 ? 255 : 20, 40, 80));
            foreach (FinalistMode mode in Enum.GetValues<FinalistMode>())
            {
                using var output = FinalistReducer.Reduce(source, mode);
                Require(output.GetPixel(0, 0).ToArgb() == Color.FromArgb(128, 20, 40, 80).ToArgb(), "Hidden RGB leaked");
            }
        });
        check("TCR phase boundaries, partial cells and tiny images have defined dimensions and colors", () =>
        {
            using var source = new Bitmap(20, 1, PixelFormat.Format32bppArgb);
            for (int x = 0; x < 20; x++) source.SetPixel(x, 0, Color.FromArgb(255, x * 10, 0, 0));
            foreach (var (mode, colors) in new[]
            {
                (FinalistMode.T3P44, new[] { 55, 150, 190 }),
                (FinalistMode.P5Off, new[] { 35, 115, 175 }),
                (FinalistMode.HalfAndHalf, new[] { 45, 133, 183 })
            })
            {
                using var output = FinalistReducer.Reduce(source, mode);
                Require(output.Size == new Size(3, 1), "Partial cell lost");
                for (int x = 0; x < 3; x++) Require(output.GetPixel(x, 0).R == colors[x], "Phase boundary changed");
            }
            foreach (int width in new[] { 1, 7, 8, 9, 12, 16, 17, 24, 33 })
            foreach (int height in new[] { 1, 8, 9, 17 })
            {
                using var tiny = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(tiny)) graphics.Clear(Color.FromArgb(255, 23, 45, 67));
                foreach (FinalistMode mode in Enum.GetValues<FinalistMode>())
                {
                    using var output = FinalistReducer.Reduce(tiny, mode);
                    Require(output.Size == new Size((width + 7) / 8, (height + 7) / 8), "Tiny dimensions changed");
                    for (int y = 0; y < output.Height; y++) for (int x = 0; x < output.Width; x++)
                        Require(output.GetPixel(x, y).ToArgb() == Color.FromArgb(255, 23, 45, 67).ToArgb(), "Empty or lost edge cell");
                }
            }
        });
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
