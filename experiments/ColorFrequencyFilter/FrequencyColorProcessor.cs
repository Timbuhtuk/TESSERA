using System.Drawing.Imaging;
using PixelArtDownscale;

namespace ColorFrequencyFilter;

internal sealed record FrequencyFilterResult(Bitmap Image, int VisiblePixels, int ColorsBefore,
    int ColorsAfter, int ReplacedColors, int ChangedPixels, double MaximumDeltaLab);

internal static class FrequencyColorProcessor
{
    public static FrequencyFilterResult Process(Bitmap source, double thresholdPercent)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!double.IsFinite(thresholdPercent) || thresholdPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(thresholdPercent), "Порог должен быть от 0 до 100%.");

        Bitmap? converted = null;
        if (source.PixelFormat is not (PixelFormat.Format24bppRgb or PixelFormat.Format32bppArgb))
        {
            converted = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
            source = converted;
        }

        try
        {
            var frequency = new Dictionary<int, int>();
            int visiblePixels = 0;
            using var read = BitmapBuffer.FromBitmap(source);
            for (int y = 0; y < read.Height; y++)
                for (int x = 0; x < read.Width; x++)
                {
                    if (read.GetAlpha(x, y) == 0) continue;
                    int color = read.GetPackedColor(x, y);
                    frequency[color] = frequency.GetValueOrDefault(color) + 1;
                    visiblePixels++;
                }

            int[] retained = visiblePixels == 0 ? [] : frequency
                .Where(pair => pair.Value * 100.0 >= thresholdPercent * visiblePixels)
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .Select(pair => pair.Key)
                .ToArray();
            if (visiblePixels > 0 && retained.Length == 0)
            {
                double maximum = frequency.Values.Max() * 100.0 / visiblePixels;
                throw new ArgumentException($"Ни один цвет не прошёл порог. Максимальная частота: {maximum:F5}%.");
            }

            var replacements = new Dictionary<int, int>();
            double maximumDelta = 0;
            if (retained.Length > 0)
            {
                var index = new LabNearestColorIndex(retained);
                foreach (int color in frequency.Keys)
                {
                    if (frequency[color] * 100.0 >= thresholdPercent * visiblePixels) continue;
                    var (nearest, delta) = index.Nearest(color);
                    replacements.Add(color, nearest);
                    maximumDelta = Math.Max(maximumDelta, delta);
                }
            }

            var result = new Bitmap(read.Width, read.Height, source.PixelFormat);
            try
            {
                int changedPixels = 0;
                using (var write = BitmapBuffer.FromWritableBitmap(result))
                {
                    for (int y = 0; y < read.Height; y++)
                        for (int x = 0; x < read.Width; x++)
                        {
                            byte alpha = read.GetAlpha(x, y);
                            int color = read.GetPackedColor(x, y);
                            if (alpha != 0 && replacements.TryGetValue(color, out int nearest))
                            {
                                color = nearest;
                                changedPixels++;
                            }
                            write.SetPackedColor(x, y, color);
                            write.SetAlpha(x, y, alpha);
                        }
                    write.Flush();
                }
                return new FrequencyFilterResult(result, visiblePixels, frequency.Count, retained.Length,
                    replacements.Count, changedPixels, maximumDelta);
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }
        finally { converted?.Dispose(); }
    }

    private sealed class LabNearestColorIndex
    {
        private readonly int[] colors;
        private readonly Point[] points;
        private readonly Node? root;

        private readonly record struct Point(int Index, double L, double A, double B)
        {
            public double Axis(int axis) => axis == 0 ? L : axis == 1 ? A : B;
        }

        private sealed record Node(Point Point, int Axis, Node? Left, Node? Right);

        public LabNearestColorIndex(int[] colors)
        {
            this.colors = colors;
            points = colors.Select((color, index) =>
            {
                var lab = ColorSpace.RgbToLab(color);
                return new Point(index, lab.L, lab.A, lab.B);
            }).ToArray();
            root = Build(0, points.Length, 0);
        }

        public (int Color, double Delta) Nearest(int color)
        {
            var lab = ColorSpace.RgbToLab(color);
            var target = new Point(0, lab.L, lab.A, lab.B);
            int best = int.MaxValue;
            double distance = double.MaxValue;
            Search(root, target, ref best, ref distance);
            return (colors[best], Math.Sqrt(distance));
        }

        private Node? Build(int start, int count, int depth)
        {
            if (count == 0) return null;
            int axis = depth % 3;
            Array.Sort(points, start, count, Comparer<Point>.Create((a, b) =>
            {
                int comparison = a.Axis(axis).CompareTo(b.Axis(axis));
                return comparison != 0 ? comparison : a.Index.CompareTo(b.Index);
            }));
            int middle = start + count / 2;
            return new Node(points[middle], axis, Build(start, middle - start, depth + 1),
                Build(middle + 1, start + count - middle - 1, depth + 1));
        }

        private static void Search(Node? node, Point target, ref int best, ref double distance)
        {
            if (node is null) return;
            double dl = target.L - node.Point.L, da = target.A - node.Point.A, db = target.B - node.Point.B;
            double candidate = dl * dl + da * da + db * db;
            if (candidate < distance || (candidate == distance && node.Point.Index < best))
            {
                best = node.Point.Index;
                distance = candidate;
            }
            double delta = target.Axis(node.Axis) - node.Point.Axis(node.Axis);
            Search(delta < 0 ? node.Left : node.Right, target, ref best, ref distance);
            if (delta * delta <= distance)
                Search(delta < 0 ? node.Right : node.Left, target, ref best, ref distance);
        }
    }
}
