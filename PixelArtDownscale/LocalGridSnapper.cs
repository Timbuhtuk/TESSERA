namespace PixelArtDownscale;

/// <summary>Moves restored cell patterns as rigid regions onto the canvas-origin grid.</summary>
internal static class LocalGridSnapper
{
    internal sealed record Result(int[] Pixels, List<LocalGridRegion> Regions, int Shifted, int Conflicts);
    private readonly record struct Cell(int X, int Y, int Color);

    internal static Result Process(int[] pixels, int[] backgroundPixels, int[] owner, bool[] background, Func<int, bool> isBackground,
        int width, int height, int pitch, List<LocalGridRegion> reports, double delta)
    {
        int columns = (width - 1) / pitch + 1, rows = (height - 1) / pitch + 1;
        bool uniform = true;
        for (int y = 0; y < height && uniform; y++)
            for (int x = 0; x < width; x++)
                if (pixels[y * width + x] != pixels[y / pitch * pitch * width + x / pitch * pitch]) { uniform = false; break; }
        if (uniform) return new(pixels, reports, 0, 0);

        // Reconstruct the vacated background before placing any region. Sampling the
        // old canvas directly would leave a second silhouette after a half-cell move.
        var nearest = new int[pixels.Length]; Array.Fill(nearest, -1);
        var queue = new int[pixels.Length]; int front = 0, back = 0;
        for (int q = 0; q < pixels.Length; q++)
            if (background[q]) { nearest[q] = q; queue[back++] = q; }
        while (front < back)
        {
            int q = queue[front++], x = q % width, y = q / width;
            if (x > 0) Visit(q - 1, q);
            if (x + 1 < width) Visit(q + 1, q);
            if (y > 0) Visit(q - width, q);
            if (y + 1 < height) Visit(q + width, q);
        }
        void Visit(int next, int previous)
        {
            if (nearest[next] >= 0) return;
            nearest[next] = nearest[previous]; queue[back++] = next;
        }
        var cells = new int[columns * rows]; var occupied = new bool[cells.Length];
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
            {
                int left = col * pitch, top = row * pitch;
                var votes = new Dictionary<int, double>();
                for (int y = top; y < Math.Min(height, top + pitch); y++)
                    for (int x = left; x < Math.Min(width, left + pitch); x++)
                    {
                        int q = y * width + x;
                        if (background[q]) Add(votes, backgroundPixels[q], Weight(x, y, left, top));
                    }
                int center = Math.Min(height - 1, top + pitch / 2) * width + Math.Min(width - 1, left + pitch / 2);
                cells[row * columns + col] = votes.Count > 0 ? Winner(votes) : backgroundPixels[nearest[center] >= 0 ? nearest[center] : center];
            }

        var updated = reports.ToDictionary(region => region.Id);
        var labCache = new Dictionary<int, (double L, double A, double B)>();
        double Contrast(int first, int second)
        {
            var a = Lab(first); var b = Lab(second);
            return Math.Max(Math.Sqrt(Math.Pow(a.L - b.L, 2) + Math.Pow(a.A - b.A, 2) + Math.Pow(a.B - b.B, 2)),
                Math.Abs(((uint)first >> 24) - (double)((uint)second >> 24)) * 100 / 255);
        }
        (double L, double A, double B) Lab(int color)
        {
            int rgb = color & 0xFFFFFF;
            if (!labCache.TryGetValue(rgb, out var value)) { value = ColorSpace.RgbToLab(rgb); labCache.Add(rgb, value); }
            return value;
        }
        // Low-opacity details can fall below the region detector. Keep their existing
        // global cell rather than silently removing them while replacing the background.
        var unowned = new Dictionary<int, Dictionary<int, double>>();
        for (int q = 0; q < pixels.Length; q++)
        {
            if (owner[q] != 0 || isBackground(pixels[q])) continue;
            int cell = q / width / pitch * columns + q % width / pitch;
            if (!unowned.TryGetValue(cell, out var votes)) { votes = []; unowned.Add(cell, votes); }
            Add(votes, pixels[q], 1);
        }
        foreach (var pair in unowned) { cells[pair.Key] = Winner(pair.Value); occupied[pair.Key] = true; }

        int shifted = 0;
        var conflictingCells = new HashSet<int>();
        foreach (var region in reports.OrderByDescending(region => region.Area).ThenBy(region => region.Id))
        {
            bool measured = region.Status is "restored" or "already-aligned";
            int phaseX = measured ? region.OriginX : CenteredPhase(region.Bounds.Left, region.Bounds.Width);
            int phaseY = measured ? region.OriginY : CenteredPhase(region.Bounds.Top, region.Bounds.Height);
            var votes = new Dictionary<(int X, int Y), Dictionary<int, double>>();
            var box = Rectangle.Intersect(new Rectangle(0, 0, width, height), Rectangle.FromLTRB(
                region.Bounds.Left - pitch * 2, region.Bounds.Top - pitch * 2, region.Bounds.Right + pitch * 2, region.Bounds.Bottom + pitch * 2));
            for (int y = box.Top; y < box.Bottom; y++)
                for (int x = box.Left; x < box.Right; x++)
                {
                    int q = y * width + x;
                    if (owner[q] != region.Id || isBackground(pixels[q])) continue;
                    int left = (int)Math.Floor((x - phaseX) / (double)pitch) * pitch + phaseX;
                    int top = (int)Math.Floor((y - phaseY) / (double)pitch) * pitch + phaseY;
                    if (!votes.TryGetValue((left, top), out var colors)) { colors = []; votes.Add((left, top), colors); }
                    Add(colors, pixels[q], Weight(x, y, left, top));
                }
            var pattern = votes.Select(pair => new Cell(pair.Key.X, pair.Key.Y, Winner(pair.Value))).ToArray();
            if (pattern.Length == 0) continue;
            int shiftX = 0, shiftY = 0; double best = double.PositiveInfinity;
            foreach (int dx in Shifts(phaseX))
                foreach (int dy in Shifts(phaseY))
                {
                    int clipped = 0, collisions = 0;
                    foreach (Cell cell in pattern)
                    {
                        int x = cell.X + dx, y = cell.Y + dy;
                        int before = VisibleArea(cell.X, cell.Y), after = VisibleArea(x, y);
                        clipped += Math.Max(0, before - after);
                        if (x < 0 || y < 0 || x >= width || y >= height) continue;
                        int target = y / pitch * columns + x / pitch;
                        if (occupied[target] && Contrast(cells[target], cell.Color) > delta) collisions++;
                    }
                    double score = clipped * 1e9 + collisions * 1e6 + dx * dx + dy * dy;
                    if (score < best) { best = score; shiftX = dx; shiftY = dy; }
                }
            if (shiftX != 0 || shiftY != 0) shifted++;
            updated[region.Id] = region with { OriginX = phaseX, OriginY = phaseY, ShiftX = shiftX, ShiftY = shiftY };
            foreach (Cell cell in pattern)
            {
                int x = cell.X + shiftX, y = cell.Y + shiftY;
                if (x < 0 || y < 0 || x >= width || y >= height) continue;
                int target = y / pitch * columns + x / pitch;
                if (occupied[target] && Contrast(cells[target], cell.Color) > delta)
                {
                    conflictingCells.Add(target);
                    // When two shapes cannot share a cell, retain the stronger visible
                    // detail. Every target cell still has exactly one complete ARGB sample.
                    int seed = Math.Min(height - 1, y + pitch / 2) * width + Math.Min(width - 1, x + pitch / 2);
                    int bg = nearest[seed] >= 0 ? backgroundPixels[nearest[seed]] : 0;
                    if (Contrast(cells[target], bg) >= Contrast(cell.Color, bg)) continue;
                }
                cells[target] = cell.Color; occupied[target] = true;
            }
        }
        var output = new int[pixels.Length];
        for (int y = 0; y < height; y++)
            for (int col = 0; col < columns; col++)
                Array.Fill(output, cells[y / pitch * columns + col], y * width + col * pitch, Math.Min(pitch, width - col * pitch));
        return new(output, reports.Select(region => updated[region.Id]).ToList(), shifted, conflictingCells.Count);

        int CenteredPhase(int begin, int extent)
        {
            int cells = Math.Max(1, (int)Math.Round(extent / (double)pitch));
            int origin = (int)Math.Round(begin + (extent - cells * pitch) * .5, MidpointRounding.AwayFromZero);
            return (origin % pitch + pitch) % pitch;
        }
        int[] Shifts(int phase) => phase == 0 ? [0] : new[] { -phase, pitch - phase }.OrderBy(Math.Abs).ThenBy(value => value).ToArray();
        int VisibleArea(int x, int y) => Math.Max(0, Math.Min(width, x + pitch) - Math.Max(0, x)) * Math.Max(0, Math.Min(height, y + pitch) - Math.Max(0, y));
        double Weight(int x, int y, int left, int top) => 1 + 2 * Math.Max(0, 1 - Math.Abs(x + .5 - left - pitch * .5) / pitch)
            * Math.Max(0, 1 - Math.Abs(y + .5 - top - pitch * .5) / pitch);
    }

    private static void Add(Dictionary<int, double> votes, int color, double weight)
        => votes[color] = votes.GetValueOrDefault(color) + weight;
    private static int Winner(Dictionary<int, double> votes) => votes.OrderByDescending(pair => pair.Value).ThenBy(pair => (uint)pair.Key).First().Key;
}
