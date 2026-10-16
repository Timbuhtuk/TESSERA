namespace PixelArtDownscale;

public sealed record TileSetLayout(int TileWidth, int TileHeight, int Columns, int Rows,
    int MarginX = 0, int MarginY = 0, int SpacingX = 0, int SpacingY = 0)
{
    public const int MaxTiles = 4096;
    public int Count => checked(Columns * Rows);

    public static TileSetLayout FromTileSize(int width, int height, int tileWidth, int tileHeight,
        int marginX = 0, int marginY = 0, int spacingX = 0, int spacingY = 0)
    {
        ValidateInputs(width, height, tileWidth, tileHeight, marginX, marginY, spacingX, spacingY);
        long usableWidth = width - 2L * marginX, usableHeight = height - 2L * marginY;
        long stepX = (long)tileWidth + spacingX, stepY = (long)tileHeight + spacingY;
        if (usableWidth <= 0 || usableHeight <= 0 || (usableWidth + spacingX) % stepX != 0 || (usableHeight + spacingY) % stepY != 0)
            throw new ArgumentException("Сетка не покрывает изображение целиком. Проверьте размер тайла, отступы и промежутки.");
        int columns = checked((int)((usableWidth + spacingX) / stepX));
        int rows = checked((int)((usableHeight + spacingY) / stepY));
        return ValidateCount(new(tileWidth, tileHeight, columns, rows, marginX, marginY, spacingX, spacingY));
    }

    public static TileSetLayout FromGrid(int width, int height, int columns, int rows,
        int marginX = 0, int marginY = 0, int spacingX = 0, int spacingY = 0)
    {
        ValidateInputs(width, height, columns, rows, marginX, marginY, spacingX, spacingY);
        long contentWidth = width - 2L * marginX - (columns - 1L) * spacingX;
        long contentHeight = height - 2L * marginY - (rows - 1L) * spacingY;
        if (contentWidth <= 0 || contentHeight <= 0 || contentWidth % columns != 0 || contentHeight % rows != 0)
            throw new ArgumentException("Нельзя разделить на целые пиксельные тайлы с такой сеткой.");
        return ValidateCount(new((int)(contentWidth / columns), (int)(contentHeight / rows), columns, rows,
            marginX, marginY, spacingX, spacingY));
    }

    private static TileSetLayout ValidateCount(TileSetLayout layout)
    {
        if (layout.Columns < 1 || layout.Rows < 1 || (long)layout.Columns * layout.Rows > MaxTiles)
            throw new ArgumentException($"За один импорт можно добавить от 1 до {MaxTiles} тайлов.");
        return layout;
    }

    private static void ValidateInputs(int width, int height, int first, int second, int marginX, int marginY, int spacingX, int spacingY)
    {
        if (width <= 0 || height <= 0 || first <= 0 || second <= 0 || marginX < 0 || marginY < 0 || spacingX < 0 || spacingY < 0)
            throw new ArgumentException("Размеры и количество ячеек должны быть положительными, отступы и промежутки — неотрицательными.");
    }
}

public sealed record TileSetSuggestion(TileSetLayout Layout, double Score, string Reason)
{
    public override string ToString() => $"{Layout.TileWidth}×{Layout.TileHeight} px · {Layout.Columns}×{Layout.Rows} · {Layout.Count} шт.";
}

public readonly record struct TileSetRegion(int X, int Y, int Width, int Height, int Column, int Row);

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
