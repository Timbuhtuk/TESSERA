namespace Tessera.Core.Imaging;

public static class TileSetSlicer
{
    public static IReadOnlyList<TileSetRegion> GetRegions(int width, int height, TileSetLayout layout)
    {
        var valid = TileSetLayout.FromTileSize(width, height, layout.TileWidth, layout.TileHeight,
            layout.MarginX, layout.MarginY, layout.SpacingX, layout.SpacingY);
        if (valid != layout) throw new ArgumentException("Сетка не соответствует размеру изображения.");
        var regions = new List<TileSetRegion>(layout.Count);
        for (int y = 0; y < layout.Rows; y++)
            for (int x = 0; x < layout.Columns; x++)
                regions.Add(new(layout.MarginX + x * (layout.TileWidth + layout.SpacingX),
                    layout.MarginY + y * (layout.TileHeight + layout.SpacingY), layout.TileWidth, layout.TileHeight, x, y));
        return regions;
    }

    // This proposes plausible grids, rather than asserting that image dimensions uniquely define tiles.
    // Common square sizes are ranked by dimensions, then adjusted by color/alpha changes at grid lines.
    public static IReadOnlyList<TileSetSuggestion> Suggest(byte[] bgra, int width, int height,
        int marginX = 0, int marginY = 0, int spacingX = 0, int spacingY = 0)
    {
        if (width <= 0 || height <= 0 || bgra.Length != checked(width * height * 4)) throw new ArgumentException("Некорректный BGRA32 буфер.");
        var sizes = new HashSet<(int Width, int Height)>();
        for (int size = 4; size <= Math.Min(512, Math.Min(width, height)); size++) sizes.Add((size, size));
        foreach (int size in new[] { 8, 16, 32, 64, 128 })
        { sizes.Add((size, size * 2)); sizes.Add((size * 2, size)); }
        var (columns, rows) = EdgeChanges(bgra, width, height);
        double meanX = columns.Skip(1).DefaultIfEmpty(0).Average(), meanY = rows.Skip(1).DefaultIfEmpty(0).Average();
        var suggestions = new List<TileSetSuggestion>();
        foreach (var size in sizes)
        {
            TileSetLayout layout;
            try { layout = TileSetLayout.FromTileSize(width, height, size.Width, size.Height, marginX, marginY, spacingX, spacingY); }
            catch (ArgumentException) { continue; }
            if (layout.Count < 2) continue;
            double prior = size.Width == size.Height ? size.Width switch
            { 32 => 55, 16 => 43, 64 => 40, 8 => 30, 24 or 48 => 28, 128 => 24, _ => 15 } : 20;
            double evidence = BoundaryEvidence(columns, layout.MarginX, layout.TileWidth, layout.SpacingX, layout.Columns, meanX) +
                BoundaryEvidence(rows, layout.MarginY, layout.TileHeight, layout.SpacingY, layout.Rows, meanY);
            double score = prior - 8 * Math.Abs(Math.Log(layout.Count / 18.0)) + evidence * 12;
            string reason = evidence > .4 ? "На границах ячеек есть цветовые или альфа-переходы. Проверьте сетку." :
                "Подбор по размерности и распространённым размерам тайлов. Проверьте сетку.";
            suggestions.Add(new(layout, score, reason));
        }
        return suggestions.OrderByDescending(suggestion => suggestion.Score).ThenBy(suggestion => suggestion.Layout.Count).ToArray();
    }

    private static double BoundaryEvidence(double[] changes, int margin, int size, int spacing, int count, double mean)
    {
        if (count < 2 || mean < 1e-9) return 0;
        double sum = 0;
        for (int q = 1; q < count; q++) sum += changes[margin + q * (size + spacing)];
        double boundary = sum / (count - 1);
        return Math.Clamp((boundary - mean) / (boundary + mean + 1e-9), -1, 1);
    }

    private static (double[] Columns, double[] Rows) EdgeChanges(byte[] pixels, int width, int height)
    {
        var columns = new double[width]; var rows = new double[height];
        // Sample every cut line, with at most 128 samples across the other axis.
        int stepY = Math.Max(1, height / 128), stepX = Math.Max(1, width / 128);
        for (int x = 1; x < width; x++)
        {
            int samples = 0;
            for (int y = 0; y < height; y += stepY) { columns[x] += Change(pixels, (y * width + x - 1) * 4, (y * width + x) * 4); samples++; }
            columns[x] /= samples;
        }
        for (int y = 1; y < height; y++)
        {
            int samples = 0;
            for (int x = 0; x < width; x += stepX) { rows[y] += Change(pixels, ((y - 1) * width + x) * 4, (y * width + x) * 4); samples++; }
            rows[y] /= samples;
        }
        return (columns, rows);
    }

    private static double Change(byte[] pixels, int first, int second)
    {
        double alphaA = pixels[first + 3] / 255.0, alphaB = pixels[second + 3] / 255.0;
        double change = Math.Abs(alphaA - alphaB);
        for (int q = 0; q < 3; q++) change += Math.Abs(pixels[first + q] * alphaA - pixels[second + q] * alphaB) / 255;
        return change / 4;
    }
}
