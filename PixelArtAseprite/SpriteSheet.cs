namespace PixelArtAseprite;

public sealed class SpriteSheet
{
    public RgbaImage Image { get; }
    public SpriteSheetMetadata Metadata { get; }
    private SpriteSheet(RgbaImage image, SpriteSheetMetadata metadata) { Image = image; Metadata = metadata; }

    public static SpriteSheet Create(AsepriteDocument document, AsepriteExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new AsepriteExportOptions();
        if (!Enum.IsDefined(options.Layout) || options.Columns < 1 || options.Padding < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Нужны допустимая раскладка, положительное число столбцов и неотрицательный промежуток.");
        cancellationToken.ThrowIfCancellationRequested();
        int count = document.Frames.Count;
        int columns = options.Layout switch
        {
            SpriteSheetLayout.Horizontal => count,
            SpriteSheetLayout.Vertical => 1,
            _ => Math.Min(options.Columns, count)
        };
        int rows = (count + columns - 1) / columns;
        long width = checked((long)columns * document.Width + (columns - 1L) * options.Padding);
        long height = checked((long)rows * document.Height + (rows - 1L) * options.Padding);
        // Проверка сторон до произведения не позволяет экстремальному padding переполнить long.
        if (width > document.Limits.MaxSheetSide || height > document.Limits.MaxSheetSide || width > int.MaxValue || height > int.MaxValue)
            throw new InvalidDataException("Сторона спрайтлиста превышает лимит.");
        long pixels = checked(width * height);
        if (pixels > document.Limits.MaxSheetPixels || pixels > int.MaxValue / 4)
            throw new InvalidDataException("Площадь спрайтлиста превышает лимит.");
        document.Limits.CheckMemory(document.WorkingBytes + pixels * 4);
        var image = new RgbaImage((int)width, (int)height, new byte[(int)pixels * 4]);
        var frames = new List<SheetFrame>(count);
        for (int q = 0; q < count; q++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = checked((int)((q % columns) * (document.Width + (long)options.Padding)));
            int y = checked((int)((q / columns) * (document.Height + (long)options.Padding)));
            CopyCel(document.Frames[q].Cel, image.Pixels, document.Width, document.Height, x, y, image.Width, cancellationToken);
            frames.Add(new SheetFrame(q, x, y, document.Width, document.Height, document.Frames[q].DurationMs));
        }
        return new SpriteSheet(image, new SpriteSheetMetadata
        {
            Image = Path.GetFileNameWithoutExtension(document.Source) + ".png", Source = document.Source, SourceSha256 = document.SourceSha256,
            SheetSize = new ImageSize(image.Width, image.Height), FrameSize = new ImageSize(document.Width, document.Height),
            Layout = options.Layout.ToString().ToLowerInvariant(), Columns = columns, Rows = rows, Padding = options.Padding,
            TotalDurationMs = document.TotalDurationMs, Tags = document.Tags, Frames = frames.AsReadOnly()
        });
    }

    internal static void CopyCel(AsepriteCel? cel, byte[] destination, int canvasWidth, int canvasHeight,
        int frameX, int frameY, int strideWidth, CancellationToken token)
    {
        if (cel?.Image is not { } image) return;
        int x0 = Math.Max(0, (int)cel.X), y0 = Math.Max(0, (int)cel.Y);
        int x1 = Math.Min(canvasWidth, cel.X + image.Width), y1 = Math.Min(canvasHeight, cel.Y + image.Height);
        if (x0 >= x1 || y0 >= y1) return;
        int rowBytes = (x1 - x0) * 4;
        for (int y = y0; y < y1; y++)
        {
            token.ThrowIfCancellationRequested();
            int source = checked(((y - cel.Y) * image.Width + x0 - cel.X) * 4);
            int target = checked(((frameY + y) * strideWidth + frameX + x0) * 4);
            image.Pixels.AsSpan(source, rowBytes).CopyTo(destination.AsSpan(target, rowBytes));
        }
    }
}
