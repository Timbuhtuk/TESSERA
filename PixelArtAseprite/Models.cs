namespace PixelArtAseprite;

public enum SpriteSheetLayout { Horizontal, Vertical, Grid }

public sealed class AsepriteExportOptions
{
    public SpriteSheetLayout Layout { get; init; } = SpriteSheetLayout.Horizontal;
    public int Columns { get; init; } = 4;
    public int Padding { get; init; }
    public bool IncludeInspection { get; init; }
}

public sealed class AsepriteLimits
{
    public int MaxInputBytes { get; init; } = 64 * 1024 * 1024;
    public long MaxFramePixels { get; init; } = 16_777_216;
    public long MaxSheetPixels { get; init; } = 16_777_216;
    public long MaxCelBytes { get; init; } = 128 * 1024 * 1024;
    public int MaxSheetSide { get; init; } = 32768;
    public long MaxWorkingBytes { get; init; } = 256 * 1024 * 1024;

    internal void Validate()
    {
        if (MaxInputBytes < 128 || MaxFramePixels < 1 || MaxSheetPixels < 1 || MaxCelBytes < 4 ||
            MaxSheetSide < 1 || MaxWorkingBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(AsepriteLimits), "Лимиты размера и памяти должны быть положительными.");
    }

    internal void CheckMemory(long bytes)
    {
        // Резерв на таблицы декодера, строки PNG, буферы потоков и метаданные.
        if (bytes > MaxWorkingBytes - 2 * 1024 * 1024)
            throw new InvalidDataException("Превышен общий лимит рабочей памяти.");
    }
}

public sealed class RgbaImage
{
    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<byte> Rgba => Pixels;
    internal byte[] Pixels { get; }
    internal RgbaImage(int width, int height, byte[] pixels) { Width = width; Height = height; Pixels = pixels; }
}

public sealed class AsepriteCel
{
    public short X { get; internal init; }
    public short Y { get; internal init; }
    public int? LinkedFrame { get; internal init; }
    public int Type { get; internal init; }
    internal RgbaImage? Image { get; set; }
}

public sealed record AsepriteFrame(int Index, int DurationMs, AsepriteCel? Cel);
public sealed record AsepriteTag(string Name, int From, int To, string Direction, int Repeat);
public sealed record ImageSize(int W, int H);
public sealed record SheetFrame(int Index, int X, int Y, int W, int H, int DurationMs);

public sealed class AsepriteDocument
{
    public int Width { get; internal init; }
    public int Height { get; internal init; }
    public string Source { get; internal init; } = "";
    public string SourceSha256 { get; internal init; } = "";
    public string LayerName { get; internal set; } = "";
    public bool IsSrgb { get; internal set; }
    public IReadOnlyList<AsepriteFrame> Frames { get; internal set; } = Array.Empty<AsepriteFrame>();
    public IReadOnlyList<AsepriteTag> Tags { get; internal set; } = Array.Empty<AsepriteTag>();
    public IReadOnlyList<string> Warnings { get; internal set; } = Array.Empty<string>();
    public long TotalDurationMs => Frames.Sum(f => (long)f.DurationMs);
    internal AsepriteLimits Limits { get; init; } = new();
    internal long WorkingBytes { get; set; }
    internal AsepriteDocument() { }

    public RgbaImage RenderFrame(int index, CancellationToken cancellationToken = default)
    {
        if (index < 0 || index >= Frames.Count) throw new ArgumentOutOfRangeException(nameof(index));
        cancellationToken.ThrowIfCancellationRequested();
        int bytes = checked(Width * Height * 4);
        Limits.CheckMemory(WorkingBytes + bytes);
        var image = new RgbaImage(Width, Height, new byte[bytes]);
        SpriteSheet.CopyCel(Frames[index].Cel, image.Pixels, Width, Height, 0, 0, Width, cancellationToken);
        return image;
    }
}

public sealed class SpriteSheetMetadata
{
    public string Schema { get; init; } = "aseprite-offline/v1";
    public required string Image { get; init; }
    public required string Source { get; init; }
    public required string SourceSha256 { get; init; }
    public required ImageSize SheetSize { get; init; }
    public required ImageSize FrameSize { get; init; }
    public required string Layout { get; init; }
    public int Columns { get; init; }
    public int Rows { get; init; }
    public int Padding { get; init; }
    public int BorderPadding => 0;
    public bool Trimmed => false;
    public int FrameCount => Frames.Count;
    public long TotalDurationMs { get; init; }
    public required IReadOnlyList<AsepriteTag> Tags { get; init; }
    public required IReadOnlyList<SheetFrame> Frames { get; init; }
}

public sealed record AsepriteExportResult(string Source, string ImagePath, string JsonPath, string? InspectionPath,
    int FrameCount, int Width, int Height, long TotalDurationMs, IReadOnlyList<string> Warnings);
public sealed record AsepriteBatchItem(string Source, AsepriteExportResult? Result, string? Error)
{
    public bool Success => Result is not null;
}
