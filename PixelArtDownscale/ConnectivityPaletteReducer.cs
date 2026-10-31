using System.Drawing.Imaging;

namespace PixelArtDownscale;

public enum PaletteDistanceSpace { Oklab, CieLab }

public sealed class ConnectivityPaletteOptions
{
    public int TargetColors { get; init; } = 128;
    public double ConnectivityWeight { get; init; } = 0.7;
    public int TileSize { get; init; } = 32;
    public double LocalWeight { get; init; } = 0.4;
    public double DetailWeight { get; init; } = 0.5;
    public double ContextWeight { get; init; } = 0.3;
    public PaletteDistanceSpace DistanceSpace { get; init; } = PaletteDistanceSpace.Oklab;
}

public sealed record ConnectivityPaletteResult(Bitmap Image, int ColorsBefore, int ColorsAfter,
    int ComponentsBefore, int RemovedColors, int ChangedPixels);

/// <summary>Experimental palette reduction based on color area, connectedness and local context.</summary>
public static class ConnectivityPaletteReducer
{
    private static readonly (int X, int Y)[] Neighbors8 =
    [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];
    private static readonly (int X, int Y)[] Neighbors4 = [(0, -1), (-1, 0), (1, 0), (0, 1)];

    private sealed class ColorInfo(int rgb)
    {
        public int Rgb = rgb;
        public int Count;
        public long SquaredComponentSizes;
        public int LargestTileCount;
        public (double L, double A, double B) Value;
    }

    private sealed class Component(int colorIndex)
    {
        public int ColorIndex = colorIndex;
        public int Count;
        public Dictionary<int, int>? BoundaryVotes;
    }

    public static ConnectivityPaletteResult Process(Bitmap source, ConnectivityPaletteOptions options)
        => Process(source, options, progress: null);

    public static ConnectivityPaletteResult Process(Bitmap source, ConnectivityPaletteOptions options, IProgress<double>? progress = null)
    {
        var tracking = new ProcessingProgress(progress);
        var result = ProcessWithProgress(source, options, tracking);
        tracking.Report(1);
        return result;
    }

    private static ConnectivityPaletteResult ProcessWithProgress(Bitmap source, ConnectivityPaletteOptions options, ProcessingProgress tracking)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (options.TargetColors is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(options.TargetColors));
        if (options.TileSize != 0 && options.TileSize is < 4 or > 256)
            throw new ArgumentOutOfRangeException(nameof(options.TileSize));
        if (!Valid(options.ConnectivityWeight) || !Valid(options.LocalWeight) ||
            !Valid(options.DetailWeight) || !Valid(options.ContextWeight))
            throw new ArgumentOutOfRangeException(nameof(options));
        if (!Enum.IsDefined(options.DistanceSpace)) throw new ArgumentOutOfRangeException(nameof(options.DistanceSpace));

        int width = source.Width, height = source.Height;
        int[] pixels = Read(source);
        int[] colorAt = new int[pixels.Length];
        int[] componentAt = new int[pixels.Length];
        Array.Fill(colorAt, -1);
        Array.Fill(componentAt, -1);
        var colors = new List<ColorInfo>();
        var colorIds = new Dictionary<int, int>();
        var tiles = new Dictionary<long, int>();
        int tileColumns = options.TileSize == 0 ? 0 : (width + options.TileSize - 1) / options.TileSize;

        for (int q = 0; q < pixels.Length; q++)
        {
            if ((uint)pixels[q] >> 24 == 0) continue;
            int rgb = pixels[q] & 0xFFFFFF;
            if (!colorIds.TryGetValue(rgb, out int id))
            {
                id = colors.Count;
                colors.Add(new ColorInfo(rgb));
                colorIds.Add(rgb, id);
            }
            colorAt[q] = id;
            colors[id].Count++;
            if (tileColumns == 0) continue;
            int tile = (q / width / options.TileSize) * tileColumns + q % width / options.TileSize;
            long key = ((long)tile << 24) | (uint)rgb;
            tiles[key] = tiles.GetValueOrDefault(key) + 1;
        }
        if (colors.Count > 4096)
            throw new ArgumentException("Более 4096 видимых RGB-цветов: сначала выполните предварительное квантование.");
        if (colors.Count == 0) return new ConnectivityPaletteResult((Bitmap)source.Clone(), 0, 0, 0, 0, 0);
        foreach (var tile in tiles)
        {
            int id = colorIds[(int)(tile.Key & 0xFFFFFF)];
            colors[id].LargestTileCount = Math.Max(colors[id].LargestTileCount, tile.Value);
        }

        var components = new List<Component>();
        var queue = new Queue<int>();
        for (int start = 0; start < pixels.Length; start++)
        {
            int color = colorAt[start];
            if (color < 0 || componentAt[start] >= 0) continue;
            int componentId = components.Count;
            var component = new Component(color);
            components.Add(component);
            componentAt[start] = componentId;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int index = queue.Dequeue(), x = index % width, y = index / width;
                component.Count++;
                foreach (var (dx, dy) in Neighbors8)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
                    int next = ny * width + nx;
                    if (colorAt[next] != color || componentAt[next] >= 0) continue;
                    componentAt[next] = componentId;
                    queue.Enqueue(next);
                }
            }
            colors[color].SquaredComponentSizes += (long)component.Count * component.Count;
        }

        if (colors.Count <= options.TargetColors)
            return new ConnectivityPaletteResult((Bitmap)source.Clone(), colors.Count, colors.Count,
                components.Count, 0, 0);
        for (int q = 0; q < colors.Count; q++)
            colors[q].Value = options.DistanceSpace == PaletteDistanceSpace.Oklab
                ? ToOklab(colors[q].Rgb) : ToLab(colors[q].Rgb);

        var distances = new double[colors.Count][];
        for (int q = 0; q < colors.Count; q++)
        {
            distances[q] = new double[colors.Count];
            for (int e = 0; e < q; e++)
            {
                var a = colors[q].Value; var b = colors[e].Value;
                double dl = a.L - b.L, da = a.A - b.A, db = a.B - b.B;
                distances[q][e] = distances[e][q] = dl * dl + da * da + db * db;
            }
        }
        bool[] active = Enumerable.Repeat(true, colors.Count).ToArray();
        var pending = new PriorityQueue<(int Color, int Nearest), double>();
        for (int q = 0; q < colors.Count; q++) Enqueue(q);
        tracking.Report(.25);
        int remaining = colors.Count;
        while (remaining > options.TargetColors)
        {
            if (!pending.TryDequeue(out var entry, out _)) throw new InvalidOperationException("Палитра пуста.");
            if (!active[entry.Color]) continue;
            int nearest = FindNearest(entry.Color);
            if (nearest != entry.Nearest) { Enqueue(entry.Color, nearest); continue; }
            active[entry.Color] = false;
            remaining--;
            tracking.Report(.25 + .5 * (colors.Count - remaining) / Math.Max(1.0, colors.Count - options.TargetColors));
        }

        int[] globalReplacement = new int[colors.Count];
        for (int q = 0; q < colors.Count; q++)
            globalReplacement[q] = active[q] ? q : FindNearest(q);
        for (int index = 0; index < pixels.Length; index++)
        {
            int componentId = componentAt[index];
            if (componentId < 0 || active[colorAt[index]]) continue;
            int x = index % width, y = index / width;
            foreach (var (dx, dy) in Neighbors4)
            {
                int nx = x + dx, ny = y + dy;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
                int next = ny * width + nx, nextColor = colorAt[next];
                if (nextColor < 0 || !active[nextColor] ||
                    (uint)pixels[index] >> 24 != (uint)pixels[next] >> 24) continue;
                var votes = components[componentId].BoundaryVotes ??= new Dictionary<int, int>();
                votes[nextColor] = votes.GetValueOrDefault(nextColor) + 1;
            }
        }
        int[] componentReplacement = new int[components.Count];
        for (int q = 0; q < components.Count; q++)
        {
            int sourceColor = components[q].ColorIndex;
            int best = globalReplacement[sourceColor];
            var votes = components[q].BoundaryVotes;
            if (votes is not null && options.ContextWeight > 0)
            {
                int totalVotes = votes.Values.Sum();
                double bestCost = distances[sourceColor][best];
                foreach (var vote in votes)
                {
                    double agreement = (double)vote.Value / totalVotes;
                    double cost = distances[sourceColor][vote.Key] - options.ContextWeight * agreement * 0.02;
                    if (cost < bestCost) { bestCost = cost; best = vote.Key; }
                }
            }
            componentReplacement[q] = best;
        }

        tracking.Report(.75);
        int changed = 0;
        int[] output = (int[])pixels.Clone();
        for (int q = 0; q < pixels.Length; q++)
        {
            int componentId = componentAt[q];
            if (componentId < 0) continue;
            int rgb = colors[componentReplacement[componentId]].Rgb;
            if (rgb == (pixels[q] & 0xFFFFFF)) continue;
            output[q] = (pixels[q] & unchecked((int)0xFF000000)) | rgb;
            changed++;
        }
        return new ConnectivityPaletteResult(Write(output, width, height), colors.Count, remaining,
            components.Count, colors.Count - remaining, changed);

        void Enqueue(int index, int nearest = -1)
        {
            if (nearest < 0) nearest = FindNearest(index);
            var color = colors[index];
            double coherence = (double)color.SquaredComponentSizes / color.Count;
            double effectiveArea = Math.Pow(color.Count, 1 - options.ConnectivityWeight) *
                Math.Pow(coherence, options.ConnectivityWeight);
            double local = 1 + 4 * options.LocalWeight * color.LargestTileCount / Math.Max(1.0, color.Count);
            double colorDifference = Math.Pow(distances[index][nearest], 0.5 + options.DetailWeight);
            double cost = effectiveArea * local * colorDifference;
            pending.Enqueue((index, nearest), cost);
        }
        int FindNearest(int index)
        {
            int best = -1;
            double distance = double.MaxValue;
            for (int e = 0; e < colors.Count; e++)
            {
                if (e == index || !active[e]) continue;
                double value = distances[index][e];
                if (value < distance || value == distance && (best < 0 || colors[e].Rgb < colors[best].Rgb))
                { best = e; distance = value; }
            }
            if (best < 0) throw new InvalidOperationException("Нет цвета для замены.");
            return best;
        }
    }

    private static bool Valid(double value) => double.IsFinite(value) && value is >= 0 and <= 1;

    private static (double L, double A, double B) ToLab(int rgb)
    {
        var (l, a, b) = ColorSpace.RgbToLab(rgb);
        return (l / 100, a / 100, b / 100);
    }

    private static (double L, double A, double B) ToOklab(int rgb)
    {
        static double Linear(int value)
        {
            double v = value / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        double r = Linear((rgb >> 16) & 255), g = Linear((rgb >> 8) & 255), b = Linear(rgb & 255);
        double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    private static int[] Read(Bitmap source)
    {
        using var copy = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        using var buffer = BitmapBuffer.FromBitmap(copy);
        int[] pixels = new int[source.Width * source.Height];
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                pixels[y * source.Width + x] = (buffer.GetAlpha(x, y) << 24) | buffer.GetPackedColor(x, y);
        return pixels;
    }

    private static Bitmap Write(int[] pixels, int width, int height)
    {
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var buffer = BitmapBuffer.FromWritableBitmap(result);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int argb = pixels[y * width + x];
                buffer.SetPackedColor(x, y, argb & 0xFFFFFF);
                buffer.SetAlpha(x, y, (byte)((uint)argb >> 24));
            }
        return result;
    }
}
