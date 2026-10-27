using System.Drawing.Imaging;
using PixelArtDownscale;

namespace PixelArtAlignment.Tests;

internal static class FinalistInvariantChecks
{
    public static void Run(Action<string, Action> check)
    {
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
