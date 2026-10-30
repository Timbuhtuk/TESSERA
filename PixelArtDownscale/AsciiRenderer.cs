using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace PixelArtDownscale;

internal static class AsciiRenderer
{
    private const int SampleSide = 8;

    private sealed class Glyph : IDisposable
    {
        public Bitmap Image { get; }
        public double Density { get; set; }
        public double[] Shape { get; } = new double[SampleSide * SampleSide];
        public double[] Ink { get; } = new double[SampleSide * SampleSide];

        public Glyph(Bitmap image) => Image = image;
        public void Dispose() => Image.Dispose();
    }

    public static Bitmap Render(Bitmap source, int[] pixels, bool keepSourceSize, string characters,
        bool invertSource, bool invertColors, AsciiColorMode colorMode, int foregroundArgb,
        int backgroundArgb, int requestedColumns, bool dither)
    {
        using var font = new Font(FontFamily.GenericMonospace, ImageFilters.DefaultAsciiFontSize,
            FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
        var cell = ImageFilters.MeasureAsciiCell(ImageFilters.DefaultAsciiFontSize);
        int cellWidth = cell.Width, cellHeight = cell.Height;

        int columns = requestedColumns > 0 ? requestedColumns : keepSourceSize
            ? Math.Max(1, (int)Math.Round(source.Width / (double)cellWidth)) : source.Width;
        if (keepSourceSize) columns = Math.Min(columns, source.Width);
        int rows = ImageFilters.CalculateAsciiRows(source.Width, source.Height, columns, cellWidth, cellHeight);
        if (keepSourceSize) rows = Math.Min(rows, source.Height);
        long width = keepSourceSize ? source.Width : (long)columns * cellWidth;
        long height = keepSourceSize ? source.Height : (long)rows * cellHeight;
        if (width > int.MaxValue || height > int.MaxValue)
            throw new InvalidOperationException("Размер ASCII-изображения превышает размер bitmap.");

        var glyphs = new List<Glyph>();
        Bitmap? result = null;
        try
        {
            // Rasterize the actual output font: a hand-ordered ramp is not a reliable ink-density scale.
            foreach (char character in characters.Distinct())
                glyphs.Add(CreateGlyph(character, font, format, cellWidth, cellHeight, invertColors));
            using var blank = CreateGlyph(' ', font, format, cellWidth, cellHeight, invertColors);
            double maximumDensity = Math.Max(0.000001, glyphs.Max(glyph => glyph.Density));
            foreach (var glyph in glyphs)
            {
                double mean = glyph.Density;
                glyph.Density /= maximumDensity;
                for (int q = 0; q < glyph.Shape.Length; q++)
                    glyph.Shape[q] = (glyph.Shape[q] - mean) / maximumDensity;
            }
            double[] densities = glyphs.Select(glyph => glyph.Density).Distinct().Order().ToArray();
            double ditherStep = 0;
            for (int q = 1; q < densities.Length; q++)
                ditherStep = Math.Max(ditherStep, densities[q] - densities[q - 1]);

            result = new Bitmap((int)width, (int)height, PixelFormat.Format24bppRgb);
            using var output = Graphics.FromImage(result);
            output.Clear(colorMode == AsciiColorMode.Monochrome
                ? (invertColors ? Color.White : Color.Black) : Color.FromArgb(backgroundArgb));
            output.CompositingMode = CompositingMode.SourceCopy;
            output.InterpolationMode = InterpolationMode.NearestNeighbor;
            output.PixelOffsetMode = PixelOffsetMode.Half;
            var target = new double[SampleSide * SampleSide];
            var coverage = new double[target.Length];
            var flatGlyphs = new Glyph?[4096];
            for (int y = 0; y < rows; y++)
            {
                int top = (int)((long)y * height / rows);
                int bottom = (int)((long)(y + 1) * height / rows);
                for (int x = 0; x < columns; x++)
                {
                    int left = (int)((long)x * width / columns);
                    int right = (int)((long)(x + 1) * width / columns);
                    double mean = SampleCell(pixels, source.Width, source.Height, x, y, columns, rows,
                        invertSource, target, coverage);
                    double tone = dither ? AsciiDither.Adjust(mean, x, y, ditherStep) : mean;
                    Glyph glyph;
                    if (mean < 0) glyph = blank;
                    else if (columns == source.Width && rows == source.Height)
                    {
                        int toneIndex = Math.Clamp((int)Math.Round(tone * (flatGlyphs.Length - 1)), 0, flatGlyphs.Length - 1);
                        glyph = flatGlyphs[toneIndex] ??= FindGlyph(glyphs, mean, tone, target, coverage, maximumDensity);
                    }
                    else glyph = FindGlyph(glyphs, mean, tone, target, coverage, maximumDensity);
                    var destination = new Rectangle(left, top, right - left, bottom - top);
                    if (colorMode == AsciiColorMode.Monochrome)
                        output.DrawImage(glyph.Image, destination, 0, 0, cellWidth, cellHeight, GraphicsUnit.Pixel);
                    else
                    {
                        int foreground = colorMode == AsciiColorMode.Custom ? foregroundArgb :
                            SampleSourceColor(pixels, source.Width, source.Height, x, y, columns, rows, glyph.Ink);
                        DrawColoredGlyph(output, glyph.Image, destination, foreground, backgroundArgb);
                    }
                }
            }
            Bitmap completed = result;
            result = null;
            return completed;
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException)
        {
            throw new InvalidOperationException($"Не удалось создать ASCII-изображение {width} × {height} px.", ex);
        }
        finally
        {
            result?.Dispose();
            foreach (var glyph in glyphs) glyph.Dispose();
        }
    }

    private static int SampleSourceColor(int[] pixels, int width, int height, int x, int y, int columns, int rows,
        double[] ink)
    {
        double x0 = x * (double)width / columns, x1 = (x + 1d) * width / columns;
        double y0 = y * (double)height / rows, y1 = (y + 1d) * height / rows;
        int left = (int)x0, right = Math.Max(left + 1, (int)Math.Ceiling(x1));
        int top = (int)y0, bottom = Math.Max(top + 1, (int)Math.Ceiling(y1));
        double red = 0, green = 0, blue = 0, weight = 0;
        double fallbackRed = 0, fallbackGreen = 0, fallbackBlue = 0, fallbackWeight = 0;
        for (int e = top; e < Math.Min(bottom, height); e++)
            for (int f = left; f < Math.Min(right, width); f++)
            {
                int pixel = pixels[e * width + f];
                int alpha = (int)((uint)pixel >> 24);
                if (alpha == 0) continue;
                double area = (Math.Min(x1, f + 1d) - Math.Max(x0, f)) *
                    (Math.Min(y1, e + 1d) - Math.Max(y0, e));
                if (area <= 0) continue;
                int inkX = Math.Clamp((int)(((f + 0.5 - x0) / (x1 - x0)) * SampleSide), 0, SampleSide - 1);
                int inkY = Math.Clamp((int)(((e + 0.5 - y0) / (y1 - y0)) * SampleSide), 0, SampleSide - 1);
                double selected = alpha * area * ink[inkY * SampleSide + inkX];
                int r = pixel >> 16 & 255, g = pixel >> 8 & 255, b = pixel & 255;
                red += r * selected; green += g * selected; blue += b * selected; weight += selected;
                fallbackRed += r * alpha * area; fallbackGreen += g * alpha * area; fallbackBlue += b * alpha * area;
                fallbackWeight += alpha * area;
            }
        if (weight == 0)
        {
            if (fallbackWeight == 0) return -1;
            red = fallbackRed; green = fallbackGreen; blue = fallbackBlue; weight = fallbackWeight;
        }
        return Color.FromArgb((int)Math.Round(red / weight), (int)Math.Round(green / weight),
            (int)Math.Round(blue / weight)).ToArgb();
    }

    private static void DrawColoredGlyph(Graphics output, Bitmap glyph, Rectangle destination,
        int foregroundArgb, int backgroundArgb)
    {
        Color foreground = Color.FromArgb(foregroundArgb), background = Color.FromArgb(backgroundArgb);
        float red = foreground.R / 255f - background.R / 255f;
        float green = foreground.G / 255f - background.G / 255f;
        float blue = foreground.B / 255f - background.B / 255f;
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix(new[]
        {
            new[] { red, green, blue, 0f, 0f },
            new[] { 0f, 0f, 0f, 0f, 0f },
            new[] { 0f, 0f, 0f, 0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { background.R / 255f, background.G / 255f, background.B / 255f, 0f, 1f }
        }));
        output.DrawImage(glyph, destination, 0, 0, glyph.Width, glyph.Height, GraphicsUnit.Pixel, attributes);
    }

    private static Glyph CreateGlyph(char character, Font font, StringFormat format, int width, int height, bool inverted)
    {
        var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            using (var graphics = Graphics.FromImage(image))
            using (var brush = new SolidBrush(inverted ? Color.Black : Color.White))
            {
                graphics.Clear(inverted ? Color.White : Color.Black);
                graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                graphics.DrawString(character.ToString(), font, brush, PointF.Empty, format);
            }

            var pixels = new int[checked(width * height)];
            var bits = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < height; y++)
                    Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), pixels, y * width, width);
            }
            finally { image.UnlockBits(bits); }

            var glyph = new Glyph(image);
            var coverage = new double[glyph.Shape.Length];
            glyph.Density = SampleCell(pixels, width, height, 0, 0, 1, 1, inverted, glyph.Ink, coverage);
            Array.Copy(glyph.Ink, glyph.Shape, glyph.Shape.Length);
            return glyph;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static Glyph FindGlyph(List<Glyph> glyphs, double mean, double tone, double[] target, double[] coverage,
        double maximumDensity)
    {
        if (tone <= 0) return glyphs.MinBy(glyph => glyph.Density)!;
        if (tone >= 1) return glyphs.MaxBy(glyph => glyph.Density)!;
        Glyph best = glyphs[0];
        double bestScore = double.MaxValue;
        double totalCoverage = coverage.Sum();
        foreach (var glyph in glyphs)
        {
            double difference = tone - glyph.Density;
            double score = difference * difference;
            if (score > bestScore) continue;
            // Compare local contrast too, so slashes and stems can follow edges instead of averaging them away.
            double shapeError = 0;
            for (int q = 0; q < target.Length; q++)
            {
                double delta = (target[q] - mean) / maximumDensity - glyph.Shape[q];
                shapeError += delta * delta * coverage[q];
            }
            score += 0.25 * shapeError / totalCoverage;
            if (score >= bestScore) continue;
            bestScore = score;
            best = glyph;
        }
        return best;
    }

    private static double SampleCell(int[] pixels, int width, int height, int x, int y, int columns, int rows,
        bool invert, double[] target, double[] coverage)
    {
        if (columns == width && rows == height)
        {
            int argb = pixels[y * width + x];
            double alpha = ((uint)argb >> 24) / 255.0;
            double value = ColorWeights.GetNormalizedBrightness(argb & 0xFFFFFF);
            if (invert) value = 1 - value;
            Array.Fill(target, alpha > 0 ? value : 0);
            Array.Fill(coverage, alpha / target.Length);
            return alpha > 0 ? value : -1;
        }
        double brightness = 0;
        double totalCoverage = 0;
        for (int e = 0; e < SampleSide; e++)
            for (int f = 0; f < SampleSide; f++)
            {
                double x0 = (x + f / (double)SampleSide) * width / columns;
                double x1 = (x + (f + 1) / (double)SampleSide) * width / columns;
                double y0 = (y + e / (double)SampleSide) * height / rows;
                double y1 = (y + (e + 1) / (double)SampleSide) * height / rows;
                double value = 0;
                double weight = 0;
                for (int q = (int)y0; q < Math.Min(height, (int)Math.Ceiling(y1)); q++)
                    for (int r = (int)x0; r < Math.Min(width, (int)Math.Ceiling(x1)); r++)
                    {
                        int argb = pixels[q * width + r];
                        double alpha = ((uint)argb >> 24) / 255.0;
                        if (alpha <= 0) continue;
                        double area = (Math.Min(x1, r + 1) - Math.Max(x0, r)) *
                            (Math.Min(y1, q + 1) - Math.Max(y0, q));
                        double sample = ColorWeights.GetNormalizedBrightness(argb & 0xFFFFFF);
                        value += (invert ? 1 - sample : sample) * alpha * area;
                        weight += alpha * area;
                    }
                int index = e * SampleSide + f;
                target[index] = weight > 0 ? value / weight : 0;
                coverage[index] = weight;
                brightness += value;
                totalCoverage += weight;
            }
        // Hidden RGB does not affect the result; a fully transparent cell always uses an empty glyph.
        return totalCoverage > 0 ? brightness / totalCoverage : -1;
    }
}
