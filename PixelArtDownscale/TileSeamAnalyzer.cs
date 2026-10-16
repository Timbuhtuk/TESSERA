namespace PixelArtDownscale;

public sealed record SeamSummary(double Mean, double Maximum, int AboveThreshold, int Count);

public sealed record TileSeamAnalysis(double[] Horizontal, double[] Vertical)
{
    public static SeamSummary Summarize(double[] differences, double threshold)
    {
        if (!double.IsFinite(threshold) || threshold < 0) throw new ArgumentOutOfRangeException(nameof(threshold));
        double sum = 0, maximum = 0;
        int above = 0;
        foreach (double value in differences)
        {
            sum += value;
            maximum = Math.Max(maximum, value);
            if (value > threshold) above++;
        }
        return new(differences.Length == 0 ? 0 : sum / differences.Length, maximum, above, differences.Length);
    }
}

public static class TileSeamAnalyzer
{
    // BGRA32, straight alpha. X joins left/right at the same y; Y joins top/bottom at the same x.
    // Only the four edge strips are needed, so analysis is O(width + height).
    public static TileSeamAnalysis AnalyzeEdges(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right,
        ReadOnlySpan<byte> top, ReadOnlySpan<byte> bottom)
    {
        if (left.Length == 0 || top.Length == 0 || left.Length % 4 != 0 || top.Length % 4 != 0 ||
            left.Length != right.Length || top.Length != bottom.Length)
            throw new ArgumentException("Edges must contain matching, nonempty BGRA32 pixel pairs.");
        var horizontal = new double[left.Length / 4];
        var vertical = new double[top.Length / 4];
        for (int q = 0; q < horizontal.Length; q++)
            horizontal[q] = ColorDistance(left.Slice(q * 4, 4), right.Slice(q * 4, 4));
        for (int q = 0; q < vertical.Length; q++)
            vertical[q] = ColorDistance(top.Slice(q * 4, 4), bottom.Slice(q * 4, 4));
        return new(horizontal, vertical);
    }

    // 100 * Euclidean distance in OKLab. Black/white is approximately 100.
    // Compare composited colors over BOTH black and white and keep the larger difference.
    // This ignores hidden RGB at alpha=0 while detecting opacity mismatches on either background.
    private static double ColorDistance(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        double black = Distance(ToLab(first, 0), ToLab(second, 0));
        double white = Distance(ToLab(first, 1), ToLab(second, 1));
        return 100 * Math.Max(black, white);
    }

    private static double Distance((double L, double A, double B) first, (double L, double A, double B) second)
    {
        double l = first.L - second.L, a = first.A - second.A, b = first.B - second.B;
        return Math.Sqrt(l * l + a * a + b * b);
    }

    // Linear sRGB -> OKLab, Bjorn Ottosson's 2021 matrices (public domain):
    // https://bottosson.github.io/posts/oklab/
    private static (double L, double A, double B) ToLab(ReadOnlySpan<byte> pixel, double background)
    {
        double alpha = pixel[3] / 255.0;
        double r = Linear(pixel[2]) * alpha + background * (1 - alpha);
        double g = Linear(pixel[1]) * alpha + background * (1 - alpha);
        double b = Linear(pixel[0]) * alpha + background * (1 - alpha);
        double l = Math.Cbrt(.4122214708 * r + .5363325363 * g + .0514459929 * b);
        double m = Math.Cbrt(.2119034982 * r + .6806995451 * g + .1073969566 * b);
        double s = Math.Cbrt(.0883024619 * r + .2817188376 * g + .6299787005 * b);
        return (.2104542553 * l + .7936177850 * m - .0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + .4505937099 * s,
            .0259040371 * l + .7827717662 * m - .8086757660 * s);
    }

    private static double Linear(byte channel)
    {
        double value = channel / 255.0;
        return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
    }
}
