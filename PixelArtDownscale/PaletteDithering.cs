namespace PixelArtDownscale;

// Serpentine Floyd-Steinberg diffusion, in linear light, restricted to the actual palette.
internal static class PaletteDithering
{
    public static int[] Apply(int[] pixels, int width, int height, IReadOnlyList<int> palette)
        => Apply(pixels, width, height, palette, progress: null);

    public static int[] Apply(int[] pixels, int width, int height, IReadOnlyList<int> palette, IProgress<double>? progress = null)
    {
        var tracking = new ProcessingProgress(progress);
        var result = ApplyWithProgress(pixels, width, height, palette, tracking);
        tracking.Report(1);
        return result;
    }

    private static int[] ApplyWithProgress(int[] pixels, int width, int height, IReadOnlyList<int> palette, ProcessingProgress tracking)
    {
        int progressRows0 = 0;
        var index = new LabPaletteIndex(palette);
        var linear = palette.Select(ColorSpace.ToLinear).ToArray();
        int[] output = new int[pixels.Length];
        var current = new double[(width + 2) * 3];
        var next = new double[current.Length];
        for (int y = 0; y < height; y++)
        {
            int direction = y % 2 == 0 ? 1 : -1;
            for (int q = 0; q < width; q++)
            {
                int x = direction == 1 ? q : width - q - 1;
                int position = y * width + x;
                int argb = pixels[position];
                output[position] = argb;
                if ((uint)argb >> 24 == 0) continue;
                int errorIndex = (x + 1) * 3;
                var original = ColorSpace.ToLinear(argb & 0xFFFFFF);
                double r = Math.Clamp(original.R + current[errorIndex], 0, 1);
                double g = Math.Clamp(original.G + current[errorIndex + 1], 0, 1);
                double b = Math.Clamp(original.B + current[errorIndex + 2], 0, 1);
                int selected = index.Nearest(ColorSpace.FromLinear(r, g, b));
                output[position] = (argb & unchecked((int)0xFF000000)) | palette[selected];
                // Transparent pixels are barriers: do not diffuse invisible RGB across a cutout.
                Spread(x + direction, y, current, 7.0 / 16);
                Spread(x - direction, y + 1, next, 3.0 / 16);
                Spread(x, y + 1, next, 5.0 / 16);
                Spread(x + direction, y + 1, next, 1.0 / 16);

                void Spread(int targetX, int targetY, double[] errors, double weight)
                {
                    if (targetX < 0 || targetX >= width || targetY >= height ||
                        (uint)pixels[targetY * width + targetX] >> 24 == 0) return;
                    int target = (targetX + 1) * 3;
                    errors[target] += (r - linear[selected].R) * weight;
                    errors[target + 1] += (g - linear[selected].G) * weight;
                    errors[target + 2] += (b - linear[selected].B) * weight;
                }
            }
            (current, next) = (next, current);
            Array.Clear(next);

            tracking.Report((0 + Interlocked.Increment(ref progressRows0) / (double)height) / 1.0);
        }
        return output;
    }
}
