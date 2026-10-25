using System.Drawing;
using System.Drawing.Imaging;

namespace PixelArtDownscale;

public static class DarkContourNoiseCleaner
{
    public sealed record Options(double DarkLightness, int MaximumDarkNeighbors,
        double BrightColorTolerance, double MinimumLightnessGain, int AlphaClearance = 1,
        double MinimumBrightConsensus = 0.6, int MaximumCardinalDarkNeighbors = 2,
        int ThreeNeighborMinimumDark5 = 9, double ThreeNeighborMinimumComponentRatio = 0.1,
        double CardinalColorWeight = 3, bool UseOppositeBrightPair = true,
        int MinimumPairExtension = 1, double PairColorTolerance = 8,
        int TwoCardinalMinimumDark5 = 11, double TwoCardinalMinimumComponentRatio = 0.4,
        int ThreeNeighborProtectedContinuation = 3, double ThreeNeighborProtectedComponentRatio = 0.2,
        double ThreeNeighborProtectedMinimumComponentRatio = 0.15,
        double RepeatedComponentSimilarity = 0.8, int RepeatedPatchRadius = 3,
        int RepeatedPatchMaximumDifferences = 2, int LargePatchRadius = 3,
        int LargePatchMinimumBrightMatches = 2, double LargePatchBrightRatio = 1,
        int SmallPatchRadius = 1, double SmallPatchMaximumComponentRatio = 0.05,
        int SmallPatchMinimumBrightMatches = 10, double SmallPatchBrightRatio = 3,
        bool AllowDiagonalBridge = true, double DiagonalBridgeMinimumConsensus = 0.9);

    public sealed record Result(Bitmap Image, int ChangedPixels);
    private readonly record struct NeighborColor(Color Color, double Weight);
    private readonly record struct PatchCounts(int Bright, int Dark);
    private sealed record DarkComponent(int[] Pixels, int MinX, int MinY, int MaxX, int MaxY);

    public static Result Clean(Bitmap source, Options options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (!double.IsFinite(options.DarkLightness) || options.DarkLightness is < 0 or > 100 ||
            options.MaximumDarkNeighbors is < 0 or > 8 ||
            options.MaximumCardinalDarkNeighbors is < 0 or > 4 ||
            options.ThreeNeighborMinimumDark5 is < 0 or > 24 ||
            options.TwoCardinalMinimumDark5 is < 0 or > 24 ||
            !double.IsFinite(options.ThreeNeighborMinimumComponentRatio) ||
            options.ThreeNeighborMinimumComponentRatio is < 0 or > 1 ||
            !double.IsFinite(options.TwoCardinalMinimumComponentRatio) ||
            options.TwoCardinalMinimumComponentRatio is < 0 or > 1 ||
            options.ThreeNeighborProtectedContinuation is < 0 or > 8 ||
            !double.IsFinite(options.ThreeNeighborProtectedComponentRatio) ||
            options.ThreeNeighborProtectedComponentRatio is < 0 or > 1 ||
            !double.IsFinite(options.ThreeNeighborProtectedMinimumComponentRatio) ||
            options.ThreeNeighborProtectedMinimumComponentRatio is < 0 or > 1 ||
            options.ThreeNeighborProtectedMinimumComponentRatio > options.ThreeNeighborProtectedComponentRatio ||
            !double.IsFinite(options.RepeatedComponentSimilarity) ||
            options.RepeatedComponentSimilarity is < 0 or > 1 ||
            options.RepeatedPatchRadius is < 0 or > 4 ||
            options.RepeatedPatchMaximumDifferences is < 0 or > 80 ||
            options.LargePatchRadius is < 0 or > 4 ||
            options.SmallPatchRadius is < 0 or > 4 ||
            options.LargePatchMinimumBrightMatches is < 0 or > 10000 ||
            options.SmallPatchMinimumBrightMatches is < 0 or > 10000 ||
            !double.IsFinite(options.LargePatchBrightRatio) || options.LargePatchBrightRatio is < 0 or > 100 ||
            !double.IsFinite(options.SmallPatchBrightRatio) || options.SmallPatchBrightRatio is < 0 or > 100 ||
            !double.IsFinite(options.SmallPatchMaximumComponentRatio) ||
            options.SmallPatchMaximumComponentRatio is < 0 or > 1 ||
            !double.IsFinite(options.DiagonalBridgeMinimumConsensus) ||
            options.DiagonalBridgeMinimumConsensus is < 0 or > 1 ||
            !double.IsFinite(options.CardinalColorWeight) || options.CardinalColorWeight is < 1 or > 10 ||
            options.MinimumPairExtension is < 0 or > 2 ||
            !double.IsFinite(options.PairColorTolerance) || options.PairColorTolerance is < 0 or > 100 ||
            !double.IsFinite(options.BrightColorTolerance) || options.BrightColorTolerance is < 0 or > 100 ||
            !double.IsFinite(options.MinimumLightnessGain) || options.MinimumLightnessGain is < 0 or > 100 ||
            options.AlphaClearance is < 1 or > 15 ||
            !double.IsFinite(options.MinimumBrightConsensus) || options.MinimumBrightConsensus is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Некорректные параметры очистки тёмных контуров.");
        int width = source.Width, height = source.Height;
        var original = new Color[width * height];
        var dark = new bool[original.Length];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                Color color = source.GetPixel(x, y);
                original[index] = color;
                dark[index] = color.A > 0 && Lab(color).L < options.DarkLightness;
            }
        var (componentSizes, componentIds, components) = FindDarkComponents(dark, width, height);
        int largestComponent = componentSizes.Max();
        int[] cardinalComponentSizes = DarkComponentSizes(dark, width, height, false);
        int largestCardinalComponent = cardinalComponentSizes.Max();
        Dictionary<UInt128, PatchCounts> patchCounts = options.LargePatchRadius > 0 ?
            BuildPatchCounts(original, dark, width, height, options.LargePatchRadius) : new();
        Dictionary<UInt128, PatchCounts> shortPatchCounts = options.SmallPatchRadius > 0 &&
            options.SmallPatchMaximumComponentRatio > 0 ?
            BuildPatchCounts(original, dark, width, height, options.SmallPatchRadius) : new();

        var output = (Color[])original.Clone();
        int changed = 0;
        for (int y = 1; y < height - 1; y++)
            for (int x = 1; x < width - 1; x++)
            {
                int index = y * width + x;
                if (!dark[index]) continue;
                if (!HasAlphaClearance(original, width, height, x, y, options.AlphaClearance)) continue;
                var surrounding = new List<NeighborColor>(8);
                int darkNeighbors = 0, cardinalDarkNeighbors = 0;
                bool allVisible = true;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int neighborIndex = (y + dy) * width + x + dx;
                        Color neighbor = original[neighborIndex];
                        if (neighbor.A == 0) { allVisible = false; continue; }
                        if (dark[neighborIndex])
                        {
                            darkNeighbors++;
                            if (dx == 0 || dy == 0) cardinalDarkNeighbors++;
                        }
                        else surrounding.Add(new NeighborColor(neighbor,
                            dx == 0 || dy == 0 ? options.CardinalColorWeight : 1));
                    }
                if (!allVisible || darkNeighbors > options.MaximumDarkNeighbors ||
                    cardinalDarkNeighbors > options.MaximumCardinalDarkNeighbors ||
                    surrounding.Count < 5) continue;
                int ringComponents = DarkRingComponents(dark, width, x, y);
                bool diagonalBridge = options.AllowDiagonalBridge && ringComponents == 2 &&
                    darkNeighbors == 2 && cardinalDarkNeighbors == 0 && cardinalComponentSizes[index] == 1;
                if (ringComponents > 1 && !diagonalBridge) continue;
                bool patchSupport = options.LargePatchRadius > 0 &&
                    HasBrightPatchMajority(original, dark, width, height, x, y, patchCounts,
                        options.LargePatchRadius, options.LargePatchMinimumBrightMatches,
                        options.LargePatchBrightRatio);
                if (!patchSupport && options.SmallPatchRadius > 0 &&
                    componentSizes[index] < largestComponent * options.SmallPatchMaximumComponentRatio)
                    patchSupport = HasBrightPatchMajority(original, dark, width, height, x, y,
                        shortPatchCounts, options.SmallPatchRadius,
                        options.SmallPatchMinimumBrightMatches, options.SmallPatchBrightRatio);
                if (darkNeighbors == 3 &&
                    (Dark5Support(dark, width, height, x, y) < options.ThreeNeighborMinimumDark5 ||
                     componentSizes[index] < largestComponent * options.ThreeNeighborMinimumComponentRatio) &&
                    !patchSupport) continue;
                if (darkNeighbors == 3 && options.ThreeNeighborProtectedContinuation > 0 &&
                    DarkContinuation(dark, width, height, x, y) >= options.ThreeNeighborProtectedContinuation &&
                    componentSizes[index] >= largestComponent * options.ThreeNeighborProtectedMinimumComponentRatio &&
                    componentSizes[index] < largestComponent * options.ThreeNeighborProtectedComponentRatio &&
                    HasRepeatedComponentPixel(original, dark, componentIds, components, width, height, x, y,
                        options.RepeatedComponentSimilarity, options.RepeatedPatchRadius,
                        options.RepeatedPatchMaximumDifferences)) continue;
                if (cardinalDarkNeighbors == 2 &&
                    (Dark5Support(dark, width, height, x, y) < options.TwoCardinalMinimumDark5 ||
                     componentSizes[index] < largestComponent * options.TwoCardinalMinimumComponentRatio ||
                     cardinalComponentSizes[index] < largestCardinalComponent *
                         options.TwoCardinalMinimumComponentRatio) && !patchSupport) continue;

                Color pairColor = default;
                bool hasPair = options.UseOppositeBrightPair && TryOppositeBrightPair(original, dark,
                    width, height, x, y, options.PairColorTolerance, options.BrightColorTolerance,
                    options.MinimumPairExtension,
                    out pairColor);
                Color replacement = hasPair ? pairColor : BrightRepresentative(surrounding, options.BrightColorTolerance);
                int matching = surrounding.Count(item => DistanceSquared(Lab(item.Color), Lab(replacement)) <=
                    options.BrightColorTolerance * options.BrightColorTolerance);
                if (!hasPair && matching / (double)surrounding.Count < options.MinimumBrightConsensus) continue;
                if (diagonalBridge && matching / (double)surrounding.Count <
                    options.DiagonalBridgeMinimumConsensus) continue;
                if (Lab(replacement).L - Lab(original[index]).L < options.MinimumLightnessGain) continue;
                output[index] = Color.FromArgb(original[index].A, replacement.R, replacement.G, replacement.B);
                changed++;
            }

        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) result.SetPixel(x, y, output[y * width + x]);
        return new Result(result, changed);
    }

    private static bool HasAlphaClearance(Color[] original, int width, int height, int x, int y, int radius)
    {
        if (x - radius < 0 || y - radius < 0 || x + radius >= width || y + radius >= height)
            return false;
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                if (original[(y + dy) * width + x + dx].A == 0) return false;
        return true;
    }

    private static int Dark5Support(bool[] dark, int width, int height, int x, int y)
    {
        int count = 0;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < width && ny < height && dark[ny * width + nx]) count++;
            }
        return count;
    }

    private static int DarkContinuation(bool[] dark, int width, int height, int x, int y)
    {
        int count = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nearX = x + dx, nearY = y + dy;
                int farX = x + 2 * dx, farY = y + 2 * dy;
                if (farX < 0 || farY < 0 || farX >= width || farY >= height) continue;
                if (dark[nearY * width + nearX] && dark[farY * width + farX]) count++;
            }
        return count;
    }

    private static Dictionary<UInt128, PatchCounts> BuildPatchCounts(Color[] colors, bool[] dark,
        int width, int height, int radius)
    {
        var counts = new Dictionary<UInt128, PatchCounts>();
        for (int y = radius; y < height - radius; y++)
            for (int x = radius; x < width - radius; x++)
            {
                int index = y * width + x;
                if (colors[index].A == 0) continue;
                UInt128 key = PatchPattern(colors, dark, width, x, y, radius);
                counts.TryGetValue(key, out PatchCounts current);
                counts[key] = dark[index] ? current with { Dark = current.Dark + 1 } :
                    current with { Bright = current.Bright + 1 };
            }
        return counts;
    }

    private static bool HasBrightPatchMajority(Color[] colors, bool[] dark, int width, int height,
        int x, int y, Dictionary<UInt128, PatchCounts> counts, int radius, int minimumBright,
        double darkMultiplier = 1)
    {
        if (x < radius || y < radius || x + radius >= width || y + radius >= height) return false;
        UInt128 key = PatchPattern(colors, dark, width, x, y, radius);
        return counts.TryGetValue(key, out PatchCounts match) &&
            match.Bright >= minimumBright && match.Bright > match.Dark * darkMultiplier;
    }

    private static UInt128 PatchPattern(Color[] colors, bool[] dark, int width, int x, int y, int radius)
    {
        UInt128 key = 0;
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int index = (y + dy) * width + x + dx;
                int value = colors[index].A == 0 ? 0 : dark[index] ? 1 : 2;
                key = key * 3 + (uint)value;
            }
        return key;
    }

    private static int[] DarkComponentSizes(bool[] dark, int width, int height, bool includeDiagonals = true)
    {
        var sizes = new int[dark.Length];
        var queue = new int[dark.Length];
        for (int q = 0; q < dark.Length; q++)
        {
            if (!dark[q] || sizes[q] != 0) continue;
            int head = 0, count = 0;
            queue[count++] = q; sizes[q] = -1;
            while (head < count)
            {
                int index = queue[head++], x = index % width, y = index / width;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        if (!includeDiagonals && Math.Abs(dx) + Math.Abs(dy) != 1) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        int neighbor = ny * width + nx;
                        if (!dark[neighbor] || sizes[neighbor] != 0) continue;
                        sizes[neighbor] = -1;
                        queue[count++] = neighbor;
                    }
            }
            for (int e = 0; e < count; e++) sizes[queue[e]] = count;
        }
        return sizes;
    }

    private static (int[] Sizes, int[] Ids, List<DarkComponent> Components) FindDarkComponents(
        bool[] dark, int width, int height)
    {
        var sizes = new int[dark.Length];
        var ids = new int[dark.Length];
        Array.Fill(ids, -1);
        var components = new List<DarkComponent>();
        var queue = new int[dark.Length];
        for (int q = 0; q < dark.Length; q++)
        {
            if (!dark[q] || ids[q] >= 0) continue;
            int id = components.Count, head = 0, count = 0;
            int minX = width, minY = height, maxX = 0, maxY = 0;
            queue[count++] = q;
            ids[q] = id;
            while (head < count)
            {
                int index = queue[head++], x = index % width, y = index / width;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        int neighbor = ny * width + nx;
                        if (!dark[neighbor] || ids[neighbor] >= 0) continue;
                        ids[neighbor] = id;
                        queue[count++] = neighbor;
                    }
            }
            int[] pixels = queue[..count].ToArray();
            foreach (int pixel in pixels) sizes[pixel] = count;
            components.Add(new DarkComponent(pixels, minX, minY, maxX, maxY));
        }
        return (sizes, ids, components);
    }

    private static bool HasRepeatedComponentPixel(Color[] colors, bool[] dark, int[] ids,
        List<DarkComponent> components,
        int width, int height, int x, int y, double similarity, int patchRadius, int maximumPatchDifferences)
    {
        int sourceId = ids[y * width + x];
        DarkComponent source = components[sourceId];
        for (int id = 0; id < components.Count; id++)
        {
            if (id == sourceId) continue;
            DarkComponent peer = components[id];
            if (Math.Abs(source.Pixels.Length - peer.Pixels.Length) > source.Pixels.Length * (1 - similarity) ||
                Math.Abs((source.MaxX - source.MinX) - (peer.MaxX - peer.MinX)) > 2 ||
                Math.Abs((source.MaxY - source.MinY) - (peer.MaxY - peer.MinY)) > 2) continue;
            int startDx = peer.MinX - source.MinX, startDy = peer.MinY - source.MinY;
            for (int dy = startDy - 1; dy <= startDy + 1; dy++)
                for (int dx = startDx - 1; dx <= startDx + 1; dx++)
                {
                    int counterpartX = x + dx, counterpartY = y + dy;
                    if (counterpartX < 0 || counterpartY < 0 || counterpartX >= width || counterpartY >= height ||
                        ids[counterpartY * width + counterpartX] != id) continue;
                    if (patchRadius > 0 &&
                        (x < patchRadius || y < patchRadius || x + patchRadius >= width ||
                         y + patchRadius >= height || counterpartX < patchRadius ||
                         counterpartY < patchRadius || counterpartX + patchRadius >= width ||
                         counterpartY + patchRadius >= height ||
                         PatchDifference(colors, dark, width, x, y, counterpartX, counterpartY,
                             patchRadius) > maximumPatchDifferences)) continue;
                    int overlap = 0;
                    foreach (int pixel in source.Pixels)
                    {
                        int nx = pixel % width + dx, ny = pixel / width + dy;
                        if (nx >= 0 && ny >= 0 && nx < width && ny < height && ids[ny * width + nx] == id)
                            overlap++;
                    }
                    if (overlap >= Math.Max(source.Pixels.Length, peer.Pixels.Length) * similarity) return true;
                }
        }
        return false;
    }

    private static int PatchDifference(Color[] colors, bool[] dark, int width,
        int x, int y, int otherX, int otherY, int radius)
    {
        int differences = 0;
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int a = (y + dy) * width + x + dx;
                int b = (otherY + dy) * width + otherX + dx;
                int classA = colors[a].A == 0 ? 0 : dark[a] ? 1 : 2;
                int classB = colors[b].A == 0 ? 0 : dark[b] ? 1 : 2;
                if (classA != classB) differences++;
            }
        return differences;
    }

    private static bool TryOppositeBrightPair(Color[] original, bool[] dark, int width, int height,
        int x, int y, double pairTolerance, double extensionTolerance, int minimumExtension, out Color color)
    {
        bool TryAxis(int dx, int dy, out Color candidate, out int extension)
        {
            candidate = default;
            extension = 0;
            int first = (y - dy) * width + x - dx, second = (y + dy) * width + x + dx;
            Color a = original[first], b = original[second];
            if (a.A == 0 || b.A != a.A || dark[first] || dark[second] ||
                DistanceSquared(Lab(a), Lab(b)) > pairTolerance * pairTolerance) return false;
            candidate = a;
            for (int side = -1; side <= 1; side += 2)
            {
                int nx = x + side * dx * 2, ny = y + side * dy * 2;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                int index = ny * width + nx;
                Color outer = original[index];
                if (outer.A == a.A && !dark[index] &&
                    DistanceSquared(Lab(outer), Lab(candidate)) <= extensionTolerance * extensionTolerance) extension++;
            }
            return extension >= minimumExtension;
        }

        bool horizontal = TryAxis(1, 0, out Color h, out int hs);
        bool vertical = TryAxis(0, 1, out Color v, out int vs);
        color = default;
        if (!horizontal && !vertical) return false;
        if (horizontal && vertical && hs == vs &&
            DistanceSquared(Lab(h), Lab(v)) > pairTolerance * pairTolerance) return false;
        color = horizontal && (!vertical || hs >= vs) ? h : v;
        return true;
    }

    private static Color BrightRepresentative(List<NeighborColor> colors, double tolerance)
    {
        int selected = 0;
        double best = double.NegativeInfinity;
        for (int q = 0; q < colors.Count; q++)
        {
            var candidate = Lab(colors[q].Color);
            double support = 0;
            foreach (NeighborColor color in colors)
            {
                var lab = Lab(color.Color);
                double distance = Math.Sqrt((candidate.L - lab.L) * (candidate.L - lab.L) +
                    (candidate.A - lab.A) * (candidate.A - lab.A) +
                    (candidate.B - lab.B) * (candidate.B - lab.B));
                support += color.Weight * Math.Max(0, 1 - distance / Math.Max(tolerance, 1));
            }
            if (support > best) { best = support; selected = q; }
        }
        return colors[selected].Color;
    }

    private static int DarkRingComponents(bool[] dark, int width, int x, int y)
    {
        Span<bool> present = stackalloc bool[9], seen = stackalloc bool[9];
        present.Clear();
        seen.Clear();
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (dx != 0 || dy != 0)
                    present[(dy + 1) * 3 + dx + 1] = dark[(y + dy) * width + x + dx];
        int components = 0;
        Span<int> queue = stackalloc int[8];
        for (int q = 0; q < 9; q++)
        {
            if (!present[q] || seen[q]) continue;
            components++;
            int head = 0, count = 0;
            queue[count++] = q; seen[q] = true;
            while (head < count)
            {
                int pixel = queue[head++], py = pixel / 3, px = pixel % 3;
                for (int e = Math.Max(0, py - 1); e <= Math.Min(2, py + 1); e++)
                    for (int f = Math.Max(0, px - 1); f <= Math.Min(2, px + 1); f++)
                    {
                        int neighbor = e * 3 + f;
                        if (neighbor == 4 || !present[neighbor] || seen[neighbor]) continue;
                        seen[neighbor] = true;
                        queue[count++] = neighbor;
                    }
            }
        }
        return components;
    }

    private static (double L, double A, double B) Lab(Color color)
        => ColorSpace.RgbToLab(color.ToArgb() & 0xFFFFFF);

    private static double DistanceSquared((double L, double A, double B) a, (double L, double A, double B) b)
    {
        double l = a.L - b.L, c = a.A - b.A, d = a.B - b.B;
        return l * l + c * c + d * d;
    }
}
