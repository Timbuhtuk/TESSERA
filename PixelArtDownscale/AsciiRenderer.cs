using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace PixelArtDownscale;

internal static class AsciiRenderer
{
    private const int SampleSide = 4;

    private sealed class Glyph : IDisposable
    {
        public Bitmap Image { get; }
        public double Density { get; set; }
        public double[] Shape { get; } = new double[SampleSide * SampleSide];

        public Glyph(Bitmap image) => Image = image;
        public void Dispose() => Image.Dispose();
    }

    public static Bitmap Render(Bitmap source, int[] pixels, bool keepSourceSize, string characters,
        bool invertSource, bool invertColors)
    {
        using var font = new Font(FontFamily.GenericMonospace, ImageFilters.DefaultAsciiFontSize,
            FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
        int cellWidth;
        int cellHeight;
        using (var measure = new Bitmap(1, 1))
        using (var graphics = Graphics.FromImage(measure))
        {
            graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            cellWidth = Math.Max(1, (int)Math.Ceiling(graphics.MeasureString("@", font, PointF.Empty, format).Width));
            cellHeight = Math.Max(1, (int)Math.Ceiling(font.GetHeight(graphics)));
        }

        int columns = keepSourceSize ? Math.Max(1, (int)Math.Round(source.Width / (double)cellWidth)) : source.Width;
        int rows = keepSourceSize ? Math.Max(1, (int)Math.Round(source.Height / (double)cellHeight)) : source.Height;
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

            result = new Bitmap((int)width, (int)height, PixelFormat.Format24bppRgb);
            using var output = Graphics.FromImage(result);
            output.Clear(invertColors ? Color.White : Color.Black);
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
                    Glyph glyph;
                    if (mean < 0) glyph = blank;
                    else if (!keepSourceSize)
                    {
                        int tone = Math.Clamp((int)Math.Round(mean * (flatGlyphs.Length - 1)), 0, flatGlyphs.Length - 1);
                        glyph = flatGlyphs[tone] ??= FindGlyph(glyphs, mean, target, coverage);
                    }
                    else glyph = FindGlyph(glyphs, mean, target, coverage);
                    output.DrawImage(glyph.Image, new Rectangle(left, top, right - left, bottom - top),
                        0, 0, cellWidth, cellHeight, GraphicsUnit.Pixel);
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
            glyph.Density = SampleCell(pixels, width, height, 0, 0, 1, 1, inverted, glyph.Shape, coverage);
            return glyph;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static Glyph FindGlyph(List<Glyph> glyphs, double mean, double[] target, double[] coverage)
    {
        if (mean <= 0) return glyphs.MinBy(glyph => glyph.Density)!;
        if (mean >= 1) return glyphs.MaxBy(glyph => glyph.Density)!;
        Glyph best = glyphs[0];
        double bestScore = double.MaxValue;
        double totalCoverage = coverage.Sum();
        foreach (var glyph in glyphs)
        {
            double difference = mean - glyph.Density;
            double score = difference * difference;
            if (score > bestScore) continue;
            // Compare local contrast too, so slashes and stems can follow edges instead of averaging them away.
            double shapeError = 0;
            for (int q = 0; q < target.Length; q++)
            {
                double delta = (target[q] - mean) - glyph.Shape[q];
                shapeError += delta * delta * coverage[q];
            }
            score += 0.08 * shapeError / totalCoverage;
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
