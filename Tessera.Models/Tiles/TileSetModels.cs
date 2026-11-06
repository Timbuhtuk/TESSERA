namespace Tessera.Models;

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
