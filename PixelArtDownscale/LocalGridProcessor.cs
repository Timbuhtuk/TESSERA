
namespace PixelArtDownscale;

public sealed record LocalGridOptions(int CellSize = 5, double RegionDelta = 8, bool Adaptive = true);
public sealed record LocalGridRegion(int Id, Rectangle Bounds, int Area, int OriginX, int OriginY,
    double ConfidenceX, double ConfidenceY, int ChangedPixels, string Status, int PreservedAnchors = 0);
public sealed record LocalGridResult(Bitmap Image, int ChangedPixels, IReadOnlyList<LocalGridRegion> Regions);

public static class LocalGridProcessor
{
    private readonly record struct Lab(double L, double A, double B)
    {
        public double Distance(Lab other) => Math.Sqrt((L - other.L) * (L - other.L) +
            (A - other.A) * (A - other.A) + (B - other.B) * (B - other.B));
    }

    private sealed class Region(int id)
    {
        public int Id = id, Area, Left = int.MaxValue, Top = int.MaxValue, Right, Bottom;
        public Rectangle Bounds => Rectangle.FromLTRB(Left, Top, Right + 1, Bottom + 1);
        public void Add(int x, int y)
        {
            Area++; Left = Math.Min(Left, x); Top = Math.Min(Top, y);
            Right = Math.Max(Right, x); Bottom = Math.Max(Bottom, y);
        }
    }

    private sealed record Anchor(int Owner, int Color, double X, double Y, double Salience);

    public static LocalGridResult Process(Bitmap source, LocalGridOptions options)
    {
        int pitch = options.CellSize;
        if (pitch is < 2 or > 64) throw new ArgumentOutOfRangeException(nameof(options.CellSize));
        if (!double.IsFinite(options.RegionDelta) || options.RegionDelta is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(options.RegionDelta));
        int width = source.Width, height = source.Height;
        int[] input = LocalDeltaProcessor.Read(source), output = (int[])input.Clone();
        var lookup = new Dictionary<int, Lab>();
        var labs = new Lab[input.Length];
        var alpha = new byte[input.Length];
        int maximumAlpha = 0;
        for (int q = 0; q < input.Length; q++)
        {
            alpha[q] = (byte)((uint)input[q] >> 24); maximumAlpha = Math.Max(maximumAlpha, alpha[q]);
            int rgb = input[q] & 0xFFFFFF;
            if (!lookup.TryGetValue(rgb, out Lab lab))
            {
                var value = ColorSpace.RgbToLab(rgb);
                lab = new Lab(value.L, value.A, value.B); lookup.Add(rgb, lab);
            }
            labs[q] = lab;
        }
        int minimumAlpha = Math.Max(1, (int)(maximumAlpha * .75));
        var parent = new int[input.Length]; var size = new int[input.Length]; var sums = (Lab[])labs.Clone();
        int valid = 0;
        for (int q = 0; q < parent.Length; q++)
        {
            parent[q] = q;
            if (alpha[q] >= minimumAlpha) { size[q] = 1; valid++; }
        }
        int Find(int q)
        {
            while (parent[q] != q) { parent[q] = parent[parent[q]]; q = parent[q]; }
            return q;
        }
        void Join(int first, int second)
        {
            if (size[first] == 0 || size[second] == 0 || Math.Abs(alpha[first] - alpha[second]) > 32 ||
                labs[first].Distance(labs[second]) > options.RegionDelta) return;
            int a = Find(first), b = Find(second);
            if (a == b) return;
            var meanA = new Lab(sums[a].L / size[a], sums[a].A / size[a], sums[a].B / size[a]);
            var meanB = new Lab(sums[b].L / size[b], sums[b].A / size[b], sums[b].B / size[b]);
            // Prevent a long chain of small gradient steps from joining a bright
            // object to a dark background through an antialiased boundary.
            if (meanA.Distance(meanB) > options.RegionDelta) return;
            if (size[a] < size[b]) (a, b) = (b, a);
            parent[b] = a; size[a] += size[b];
            sums[a] = new Lab(sums[a].L + sums[b].L, sums[a].A + sums[b].A, sums[a].B + sums[b].B);
        }
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int q = y * width + x;
                if (size[q] == 0) continue;
                if (x > 0) Join(q, q - 1);
                if (y > 0) Join(q, q - width);
            }
        var backdrop = new HashSet<int>();
        // Large smooth regions on an opaque canvas act as backgrounds. Transparent
        // canvases instead use transparency, so a monochrome sprite remains a region.
        var flatRegions = new Dictionary<int, Region>();
        for (int q = 0; q < input.Length; q++)
        {
            if (alpha[q] < minimumAlpha) continue;
            int root = Find(q);
            if (!flatRegions.TryGetValue(root, out Region? flat)) { flat = new Region(root); flatRegions.Add(root, flat); }
            flat.Add(q % width, q / width);
        }
        foreach (var pair in flatRegions)
        {
            Region flat = pair.Value;
            bool wideBackdrop = flat.Bounds.Width >= width * .7 && flat.Bounds.Height >= height * .35 && flat.Area >= valid * .45;
            if (flat.Area >= input.Length * .06 && (valid >= input.Length * .65 || wideBackdrop)) backdrop.Add(pair.Key);
        }
        var foreground = new bool[input.Length];
        for (int q = 0; q < input.Length; q++)
            foreground[q] = alpha[q] >= minimumAlpha && !backdrop.Contains(Find(q));
        var owner = new int[input.Length];
        var regions = new List<Region>(); var queue = new int[input.Length];
        for (int q = 0; q < foreground.Length; q++)
        {
            if (!foreground[q] || owner[q] != 0) continue;
            var region = new Region(regions.Count + 1); regions.Add(region);
            int head = 0, tail = 1; queue[0] = q; owner[q] = region.Id;
            while (head < tail)
            {
                int p = queue[head++], y = p / width, x = p % width;
                region.Add(x, y);
                for (int e = Math.Max(0, y - 1); e <= Math.Min(height - 1, y + 1); e++)
                    for (int f = Math.Max(0, x - 1); f <= Math.Min(width - 1, x + 1); f++)
                    {
                        int neighbor = e * width + f;
                        if (!foreground[neighbor] || owner[neighbor] != 0) continue;
                        owner[neighbor] = region.Id; queue[tail++] = neighbor;
                    }
            }
        }
        regions = SplitLongRegions(regions, owner, labs, alpha, width, height, pitch, options.RegionDelta);
        // Ownership is determined before restoration. A large nearby object cannot
        // overwrite the original position or pixels belonging to another region.
        var distance = new byte[input.Length]; int front = 0, back = 0;
        for (int q = 0; q < owner.Length; q++) if (owner[q] != 0) queue[back++] = q;
        int padding = pitch * 2;
        while (front < back)
        {
            int q = queue[front++];
            if (distance[q] >= padding) continue;
            int x = q % width, y = q / width;
            for (int e = Math.Max(0, y - 1); e <= Math.Min(height - 1, y + 1); e++)
                for (int f = Math.Max(0, x - 1); f <= Math.Min(width - 1, x + 1); f++)
                {
                    int p = e * width + f;
                    if (owner[p] != 0) continue;
                    owner[p] = owner[q]; distance[p] = (byte)(distance[q] + 1); queue[back++] = p;
                }
        }
        var anchors = new List<Anchor>();
        foreach (var pair in flatRegions)
        {
            Region flat = pair.Value; Rectangle bounds = flat.Bounds;
            if (backdrop.Contains(pair.Key) || flat.Area > pitch * pitch * 4 ||
                bounds.Width > pitch * 3 || bounds.Height > pitch * 3) continue;
            var points = new List<int>();
            double l = 0, a = 0, b = 0, opacity = 0, x = 0, y = 0;
            for (int py = bounds.Top; py < bounds.Bottom; py++)
                for (int px = bounds.Left; px < bounds.Right; px++)
                {
                    int q = py * width + px;
                    if (alpha[q] < minimumAlpha || Find(q) != pair.Key) continue;
                    points.Add(q); l += labs[q].L; a += labs[q].A; b += labs[q].B; opacity += alpha[q]; x += px + .5; y += py + .5;
                }
            if (points.Count == 0) continue;
            var mean = new Lab(l / points.Count, a / points.Count, b / points.Count);
            double ringL = 0, ringA = 0, ringB = 0, ringAlpha = 0; int ringCount = 0;
            int ring = Math.Max(2, pitch / 2);
            for (int py = Math.Max(0, bounds.Top - ring); py < Math.Min(height, bounds.Bottom + ring); py++)
                for (int px = Math.Max(0, bounds.Left - ring); px < Math.Min(width, bounds.Right + ring); px++)
                {
                    int q = py * width + px;
                    if (alpha[q] >= minimumAlpha && Find(q) == pair.Key) continue;
                    ringL += labs[q].L; ringA += labs[q].A; ringB += labs[q].B; ringAlpha += alpha[q]; ringCount++;
                }
            if (ringCount == 0) continue;
            double salience = Math.Max(mean.Distance(new Lab(ringL / ringCount, ringA / ringCount, ringB / ringCount)),
                Math.Abs(opacity / points.Count - ringAlpha / ringCount) * 100 / 255);
            if (salience < Math.Max(12, options.RegionDelta * 1.5)) continue;
            int representative = points.OrderBy(q => labs[q].Distance(mean)).First();
            if (mean.L > ringL / ringCount + 12) representative = points.OrderByDescending(q => labs[q].L).First();
            bool physical = points.Count < pitch * pitch * .25 && (bounds.Width < pitch * .75 || bounds.Height < pitch * .75);
            if (physical)
            {
                double nearestBoundary = double.PositiveInfinity;
                foreach (int q in points)
                {
                    int px = q % width, py = q / width;
                    for (int e = Math.Max(0, py - 1); e <= Math.Min(height - 1, py + 1); e++)
                        for (int f = Math.Max(0, px - 1); f <= Math.Min(width - 1, px + 1); f++)
                        {
                            int p = e * width + f;
                            if (alpha[p] >= minimumAlpha && Find(p) == pair.Key) continue;
                            double contrast = Math.Max(labs[q].Distance(labs[p]), Math.Abs(alpha[q] - alpha[p]) * 100.0 / 255);
                            nearestBoundary = Math.Min(nearestBoundary, contrast);
                        }
                }
                if (nearestBoundary < Math.Max(12, options.RegionDelta * 1.5))
                {
                    // A fragment of a smooth halo is not a separate one-pixel detail.
                    // Its bright core can still anchor a whole logical cell.
                    if (mean.L < 75 || mean.L < ringL / ringCount + 12) continue;
                }
            }
            int ownerId = owner[points[0]];
            if (ownerId != 0) anchors.Add(new(ownerId, input[representative], x / points.Count,
                y / points.Count, salience));
        }
        var ex = new float[input.Length]; var ey = new float[input.Length];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int q = y * width + x;
                if (x > 0) ex[q] = Edge(q, q - 1);
                if (y > 0) ey[q] = Edge(q, q - width);
            }
        ex = Coherent(ex, width, height, true); ey = Coherent(ey, width, height, false);
        float Edge(int a, int b)
        {
            if (Math.Max(alpha[a], alpha[b]) < minimumAlpha * .5) return 0;
            double color = labs[a].Distance(labs[b]) * Math.Min(alpha[a], alpha[b]) / 255;
            double opacity = Math.Abs(alpha[a] - alpha[b]) * Math.Max(alpha[a], alpha[b]) * 100.0 / (255 * 255);
            double strength = Math.Max(color, opacity);
            return strength >= Math.Max(2, options.RegionDelta * .5) ? (float)strength : 0;
        }
        var reports = new List<LocalGridRegion>();
        foreach (Region region in regions)
        {
            Rectangle bounds = region.Bounds;
            if (region.Area < Math.Max(3, pitch) || bounds.Width < pitch * .75 || bounds.Height < pitch * .75)
            {
                reports.Add(new(region.Id, bounds, region.Area, 0, 0, 0, 0, 0, "subcell-preserved")); continue;
            }
            Rectangle box = Rectangle.Intersect(new Rectangle(0, 0, width, height),
                Rectangle.FromLTRB(bounds.Left - padding, bounds.Top - padding, bounds.Right + padding, bounds.Bottom + padding));
            double[] xp = Profile(ex, true, box.Top, box.Bottom), yp = Profile(ey, false, box.Left, box.Right);
            (int phaseX, double confidenceX) = Phase(xp, pitch);
            (int phaseY, double confidenceY) = Phase(yp, pitch);
            // Sharp outlines give an explicit position anchor. Unequal interior
            // widths must not shift an entire object to a stronger Fourier phase.
            if (Spread(xp) < .12) phaseX = AnchoredPhase(bounds.Left, bounds.Width, phaseX);
            if (Spread(yp) < .12) phaseY = AnchoredPhase(bounds.Top, bounds.Height, phaseY);
            if (confidenceX < .15 || confidenceY < .15)
            {
                reports.Add(new(region.Id, bounds, region.Area, phaseX, phaseY, confidenceX, confidenceY, 0, "ambiguous-preserved")); continue;
            }
            int left = (int)Math.Floor((box.Left - phaseX) / (double)pitch) * pitch + phaseX;
            int top = (int)Math.Floor((box.Top - phaseY) / (double)pitch) * pitch + phaseY;
            int columns = (int)Math.Ceiling((box.Right - left) / (double)pitch);
            int rows = (int)Math.Ceiling((box.Bottom - top) / (double)pitch);
            double[] targetX = Enumerable.Range(0, columns + 1).Select(q => (double)(left + q * pitch)).ToArray();
            double[] targetY = Enumerable.Range(0, rows + 1).Select(q => (double)(top + q * pitch)).ToArray();
            if (Uniform())
            {
                reports.Add(new(region.Id, bounds, region.Area, phaseX, phaseY, confidenceX, confidenceY, 0, "already-aligned")); continue;
            }
            bool diffuseX = Spread(xp) > .14, diffuseY = Spread(yp) > .14;
            double offsetX = Unwrap(Measure(xp, pitch).Phase - phaseX, 0, pitch);
            double offsetY = Unwrap(Measure(yp, pitch).Phase - phaseY, 0, pitch);
            double[] gx = diffuseX ? targetX.Select(value => value + offsetX).ToArray() : Fit(xp, targetX, pitch);
            double[] gy = diffuseY ? targetY.Select(value => value + offsetY).ToArray() : Fit(yp, targetY, pitch);
            var localX = new double[rows][]; var localY = new double[columns][];
            for (int row = 0; row < rows; row++)
                localX[row] = !options.Adaptive ? gx : diffuseX
                    ? LocalPhases(Profile(ex, true, gy[row], gy[row + 1]), targetX, row == 0 ? gx : localX[row - 1], offsetX, pitch)
                    : Fit(Profile(ex, true, gy[row], gy[row + 1]), gx, pitch);
            for (int col = 0; col < columns; col++)
                localY[col] = !options.Adaptive ? gy : diffuseY
                    ? LocalPhases(Profile(ey, false, gx[col], gx[col + 1]), targetY, col == 0 ? gy : localY[col - 1], offsetY, pitch)
                    : Fit(Profile(ey, false, gx[col], gx[col + 1]), gy, pitch);
            int changed = 0;
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < columns; col++)
                {
                    int selected = Select(input, width, height, localX[row][col], localY[col][row], localX[row][col + 1], localY[col][row + 1]);
                    for (int y = Math.Max(0, (int)targetY[row]); y < Math.Min(height, targetY[row + 1]); y++)
                        for (int x = Math.Max(0, (int)targetX[col]); x < Math.Min(width, targetX[col + 1]); x++)
                        {
                            int q = y * width + x;
                            if (owner[q] != region.Id) continue;
                            if (input[q] != selected) changed++;
                            output[q] = selected;
                        }
                }
            var regionAnchors = anchors.Where(anchor => anchor.Owner == region.Id)
                .OrderByDescending(anchor => lookup[anchor.Color & 0xFFFFFF].L >= 75)
                .ThenByDescending(anchor => lookup[anchor.Color & 0xFFFFFF].L >= 75
                    ? lookup[anchor.Color & 0xFFFFFF].L : anchor.Salience).ToArray();
            var anchoredCells = new HashSet<(int Column, int Row)>();
            foreach (Anchor anchor in regionAnchors)
            {
                int col = (int)Math.Floor((anchor.X - left) / pitch), row = (int)Math.Floor((anchor.Y - top) / pitch);
                if (col < 0 || col >= columns || row < 0 || row >= rows || !anchoredCells.Add((col, row))) continue;
                bool present = false;
                int sourceAlpha = (int)((uint)anchor.Color >> 24);
                Lab sourceLab = lookup[anchor.Color & 0xFFFFFF];
                int minimumChannel = Math.Min((anchor.Color >> 16) & 255, Math.Min((anchor.Color >> 8) & 255, anchor.Color & 255));
                for (int y = Math.Max(0, (int)Math.Floor(anchor.Y - pitch * .5)); y < Math.Min(height, anchor.Y + pitch * .5) && !present; y++)
                    for (int x = Math.Max(0, (int)Math.Floor(anchor.X - pitch * .5)); x < Math.Min(width, anchor.X + pitch * .5); x++)
                    {
                        int color = output[y * width + x];
                        if ((uint)color >> 24 < sourceAlpha * .75) continue;
                        // A nearby pale halo must not count as a retained bright core.
                        int candidateMinimum = Math.Min((color >> 16) & 255, Math.Min((color >> 8) & 255, color & 255));
                        if (sourceLab.L >= 75 && candidateMinimum < minimumChannel) continue;
                        if (sourceLab.Distance(lookup[color & 0xFFFFFF]) <= Math.Min(3, options.RegionDelta * .5)) { present = true; break; }
                    }
                if (present) continue;
                for (int y = Math.Max(0, (int)targetY[row]); y < Math.Min(height, targetY[row + 1]); y++)
                    for (int x = Math.Max(0, (int)targetX[col]); x < Math.Min(width, targetX[col + 1]); x++)
                    {
                        int q = y * width + x;
                        if (owner[q] == region.Id) output[q] = anchor.Color;
                    }
            }
            changed = 0;
            for (int y = box.Top; y < box.Bottom; y++)
                for (int x = box.Left; x < box.Right; x++)
                {
                    int q = y * width + x;
                    if (owner[q] == region.Id && input[q] != output[q]) changed++;
                }
            reports.Add(new(region.Id, bounds, region.Area, phaseX, phaseY, confidenceX, confidenceY, changed, "restored", regionAnchors.Length));

            double[] Profile(float[] edge, bool horizontal, double start, double end)
            {
                int length = horizontal ? width : height, cross = horizontal ? height : width;
                var profile = new double[length + 1];
                int lo = Math.Clamp((int)Math.Floor(start), 0, cross - 1), hi = Math.Clamp((int)Math.Ceiling(end), lo + 1, cross);
                int begin = horizontal ? box.Left : box.Top, finish = horizontal ? box.Right : box.Bottom;
                for (int p = Math.Max(1, begin); p < finish; p++)
                    for (int q = lo; q < hi; q++)
                    {
                        int index = horizontal ? q * width + p : p * width + q;
                        int previous = index - (horizontal ? 1 : width);
                        if (owner[index] == region.Id || owner[previous] == region.Id) profile[p] += edge[index];
                    }
                return profile;
            }

            bool Uniform()
            {
                for (int row = 0; row < rows; row++)
                    for (int col = 0; col < columns; col++)
                    {
                        int? color = null;
                        for (int y = Math.Max(0, (int)targetY[row]); y < Math.Min(height, targetY[row + 1]); y++)
                            for (int x = Math.Max(0, (int)targetX[col]); x < Math.Min(width, targetX[col + 1]); x++)
                            {
                                int q = y * width + x;
                                if (owner[q] != region.Id) continue;
                                int argb = (uint)input[q] >> 24 == 0 ? 0 : input[q];
                                if (color is not null && color.Value != argb) return false;
                                color = argb;
                            }
                    }
                return true;
            }

            int AnchoredPhase(int begin, int extent, int fallback)
            {
                int targetExtent = Math.Max(1, (int)Math.Round(extent / (double)pitch)) * pitch;
                if (Math.Abs(extent - targetExtent) > pitch * .25) return fallback;
                int anchor = (int)Math.Round(begin + (extent - targetExtent) * .5, MidpointRounding.AwayFromZero);
                return (anchor % pitch + pitch) % pitch;
            }
        }
        int total = 0;
        for (int q = 0; q < input.Length; q++) if (input[q] != output[q]) total++;
        return new(LocalDeltaProcessor.Write(output, width, height), total, reports);
    }

    private static (int Phase, double Confidence) Phase(double[] profile, int pitch)
    {
        double real = 0, imaginary = 0, sum = 0;
        for (int q = 0; q < profile.Length; q++)
        {
            double angle = q * 2 * Math.PI / pitch;
            real += profile[q] * Math.Cos(angle); imaginary += profile[q] * Math.Sin(angle); sum += profile[q];
        }
        if (sum < 1e-8) return (0, 0);
        int phase = (int)Math.Round(Math.Atan2(imaginary, real) * pitch / (2 * Math.PI));
        return ((phase % pitch + pitch) % pitch, Math.Sqrt(real * real + imaginary * imaginary) / sum);
    }

    private static double Spread(double[] profile)
    {
        double total = profile.Sum(), adjacent = 0;
        for (int q = 1; q < profile.Length; q++) adjacent += Math.Min(profile[q - 1], profile[q]);
        return total < 1e-8 ? 1 : adjacent / total;
    }

    private static double Unwrap(double value, double reference, int pitch)
        => value - Math.Round((value - reference) / pitch) * pitch;

    private static (double Phase, double Confidence) Measure(double[] profile, int pitch, double? center = null)
    {
        double real = 0, imaginary = 0, sum = 0;
        for (int q = 0; q < profile.Length; q++)
        {
            double weight = center is null ? 1 : Math.Exp(-.5 * Math.Pow((q - center.Value) / (pitch * 3.0), 2));
            double value = profile[q] * weight, angle = q * 2 * Math.PI / pitch;
            real += value * Math.Cos(angle); imaginary += value * Math.Sin(angle); sum += value;
        }
        return sum < 1e-8 ? (0, 0) : (Math.Atan2(imaginary, real) * pitch / (2 * Math.PI), Math.Sqrt(real * real + imaginary * imaginary) / sum);
    }

    private static double[] LocalPhases(double[] profile, double[] targets, double[] nominal, double globalOffset, int pitch)
    {
        double[] result = (double[])nominal.Clone();
        if (targets.Length <= 2 || Measure(profile, pitch).Confidence < .1) return result;
        var measured = new double[targets.Length - 2];
        double origin = targets[0] % pitch;
        bool initial = true;
        for (int q = 1; q < targets.Length - 1; q++)
        {
            measured[q - 1] = Measure(profile, pitch, nominal[q]).Phase - origin;
            if (Math.Abs(nominal[q] - targets[q] - globalOffset) > 1e-8) initial = false;
        }
        double left = nominal[1] - targets[1], right = nominal[^2] - targets[^2];
        double leftDistance = Math.Abs(Unwrap(measured[0], left, pitch) - left);
        double rightDistance = Math.Abs(Unwrap(measured[^1], right, pitch) - right);
        bool reverse = initial && leftDistance >= pitch * .49 && rightDistance < leftDistance;
        int start = reverse ? targets.Length - 2 : 1;
        for (int pass = 0; pass < 2; pass++)
        {
            bool saturated = true;
            double reference = pass == 0 ? nominal[start] - targets[start] : globalOffset;
            for (int q = 0; q < measured.Length; q++)
            {
                int i = reverse ? start - q : start + q;
                double offset = Unwrap(measured[i - 1], reference, pitch);
                double clipped = Math.Clamp(offset, globalOffset - pitch * .7, globalOffset + pitch * .7);
                if (offset == clipped) saturated = false;
                result[i] = targets[i] + clipped; reference = clipped;
            }
            if (!saturated) break;
        }
        for (int q = 1; q < result.Length - 1; q++)
            result[q] = Math.Clamp(result[q], result[q - 1] + pitch * .25, result[^1] - (result.Length - 1 - q) * pitch * .25);
        return result;
    }

    private static float[] Coherent(float[] edge, int width, int height, bool horizontal)
    {
        var result = new float[edge.Length];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int q = y * width + x;
                float a = edge[horizontal ? Math.Max(0, y - 1) * width + x : y * width + Math.Max(0, x - 1)];
                float b = edge[q];
                float c = edge[horizontal ? Math.Min(height - 1, y + 1) * width + x : y * width + Math.Min(width - 1, x + 1)];
                result[q] = a + b + c - Math.Min(a, Math.Min(b, c)) - Math.Max(a, Math.Max(b, c));
            }
        return result;
    }

    private static List<Region> SplitLongRegions(List<Region> regions, int[] owner, Lab[] labs, byte[] alpha,
        int width, int height, int pitch, double delta)
    {
        var splits = new Dictionary<int, (bool Horizontal, int[] Cuts)>();
        foreach (Region region in regions)
        {
            Rectangle bounds = region.Bounds;
            bool horizontal = bounds.Width >= bounds.Height;
            int length = horizontal ? bounds.Width : bounds.Height, breadth = horizontal ? bounds.Height : bounds.Width;
            if (length < pitch * 24 || length < breadth * 6) continue;
            int start = horizontal ? bounds.Left : bounds.Top;
            var profile = new double[length];
            for (int y = bounds.Top; y < bounds.Bottom; y++)
                for (int x = bounds.Left; x < bounds.Right; x++)
                {
                    int q = y * width + x, previous = q - (horizontal ? 1 : width);
                    if (owner[q] != region.Id || previous < 0 || (horizontal && x == 0)) continue;
                    double color = labs[q].Distance(labs[previous]) * Math.Min(alpha[q], alpha[previous]) / 255;
                    double opacity = Math.Abs(alpha[q] - alpha[previous]) * 100.0 / 255;
                    if (Math.Max(color, opacity) >= Math.Max(12, delta * 1.5)) profile[(horizontal ? x : y) - start]++;
                }
            var smooth = new double[length];
            for (int q = 0; q < length; q++)
                for (int e = Math.Max(0, q - pitch); e <= Math.Min(length - 1, q + pitch); e++) smooth[q] += profile[e];
            double maximum = smooth.Max();
            if (maximum < pitch * 2) continue;
            var active = smooth.Select(value => value >= maximum * .15).ToArray();
            int last = -1;
            for (int q = 0; q < length; q++)
            {
                if (!active[q]) continue;
                if (last >= 0 && q - last <= pitch * 2)
                    for (int e = last; e < q; e++) active[e] = true;
                last = q;
            }
            var runs = new List<(int Left, int Right)>();
            for (int q = 0; q < length; q++)
            {
                if (!active[q]) continue;
                int left = q;
                while (q + 1 < length && active[q + 1]) q++;
                if (q - left + 1 >= pitch) runs.Add((left, q + 1));
            }
            if (runs.Count < 2 || !runs.Zip(runs.Skip(1), (a, b) => b.Left - a.Right).Any(gap => gap >= pitch * 8)) continue;
            var cuts = new SortedSet<int>();
            foreach (var run in runs)
            {
                if (run.Left > pitch * 4) cuts.Add(start + run.Left - pitch * 2);
                if (length - run.Right > pitch * 4) cuts.Add(start + run.Right + pitch * 2);
            }
            if (cuts.Count > 0) splits.Add(region.Id, (horizontal, cuts.ToArray()));
        }
        if (splits.Count == 0) return regions;
        var result = new List<Region>(); var ids = new Dictionary<(int Id, int Part), int>();
        for (int q = 0; q < owner.Length; q++)
        {
            int old = owner[q];
            if (old == 0) continue;
            int part = 0;
            if (splits.TryGetValue(old, out var split))
            {
                int position = split.Horizontal ? q % width : q / width;
                while (part < split.Cuts.Length && position >= split.Cuts[part]) part++;
            }
            var key = (old, part);
            if (!ids.TryGetValue(key, out int id))
            {
                id = result.Count + 1; ids.Add(key, id); result.Add(new Region(id));
            }
            owner[q] = id; result[id - 1].Add(q % width, q / width);
        }
        return result;
    }

    private static double[] Fit(double[] profile, double[] anchors, int pitch)
    {
        if (profile.Max() < 1e-8) return (double[])anchors.Clone();
        var features = new double[profile.Length];
        double floor = profile.Max() * .06;
        for (int q = 1; q < profile.Length - 1; q++)
        {
            if (profile[q] < floor) continue;
            double maximum = 0;
            for (int e = Math.Max(0, q - pitch / 3); e <= Math.Min(profile.Length - 1, q + pitch / 3); e++)
                maximum = Math.Max(maximum, profile[e]);
            features[q] = profile[q] / maximum;
        }
        int n = anchors.Length, radius = Math.Max(1, (int)Math.Ceiling(pitch * .6));
        var candidates = new int[n][]; var costs = new double[n][]; var previous = new int[n][];
        candidates[0] = [(int)anchors[0]]; costs[0] = [0]; previous[0] = [0];
        for (int q = 1; q < n; q++)
        {
            int lo = (int)Math.Round(anchors[q]) - radius;
            candidates[q] = q == n - 1 ? [(int)anchors[q]] : Enumerable.Range(lo, radius * 2 + 1).ToArray();
            costs[q] = Enumerable.Repeat(double.PositiveInfinity, candidates[q].Length).ToArray();
            previous[q] = new int[candidates[q].Length];
            for (int e = 0; e < candidates[q].Length; e++)
            {
                int p = candidates[q][e];
                double displacement = (p - anchors[q]) / pitch;
                double unary = 2 * displacement * displacement - 2.5 * Feature(p);
                for (int f = 0; f < candidates[q - 1].Length; f++)
                {
                    int left = candidates[q - 1][f], spacing = p - left;
                    if (spacing < pitch * .4 || spacing > pitch * 1.6) continue;
                    double penalty = (spacing - pitch) / (double)pitch, mismatch = 0;
                    for (int k = left + 1; k < p; k++)
                        mismatch += Feature(k) * 4 * (k - left) * (p - k) / (spacing * spacing);
                    double cost = costs[q - 1][f] + unary + .8 * penalty * penalty + 12 * mismatch;
                    if (cost < costs[q][e]) { costs[q][e] = cost; previous[q][e] = f; }
                }
            }
        }
        if (!double.IsFinite(costs[^1][0])) return (double[])anchors.Clone();
        var result = new double[n]; int selected = 0;
        for (int q = n - 1; q >= 0; q--) { result[q] = candidates[q][selected]; selected = previous[q][selected]; }
        return result;
        double Feature(int p) => p >= 0 && p < features.Length ? features[p] : 0;
    }

    private static int Select(int[] pixels, int width, int height, double x0, double y0, double x1, double y1)
    {
        double cx = (x0 + x1) * .5, cy = (y0 + y1) * .5;
        var votes = new Dictionary<int, double>();
        for (int y = Math.Max(0, (int)Math.Floor(y0)); y < Math.Min(height, (int)Math.Ceiling(y1)); y++)
            for (int x = Math.Max(0, (int)Math.Floor(x0)); x < Math.Min(width, (int)Math.Ceiling(x1)); x++)
            {
                double area = Math.Max(0, Math.Min(x + 1, x1) - Math.Max(x, x0)) * Math.Max(0, Math.Min(y + 1, y1) - Math.Max(y, y0));
                double dx = (x + .5 - cx) / (x1 - x0), dy = (y + .5 - cy) / (y1 - y0);
                int color = pixels[y * width + x];
                votes[color] = votes.GetValueOrDefault(color) + area * Math.Exp(-10 * (dx * dx + dy * dy));
            }
        return votes.Count == 0 ? pixels[Math.Clamp((int)cy, 0, height - 1) * width + Math.Clamp((int)cx, 0, width - 1)]
            : votes.OrderByDescending(q => q.Value).ThenBy(q => (uint)q.Key).First().Key;
    }
}
