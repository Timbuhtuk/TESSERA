using System.Drawing;
using System.Drawing.Imaging;
using PixelArtDownscale;

namespace Pixelizator.Cli;

internal static partial class ImageToolsCommand
{
    internal const string InspectTilesHelp = """
        Использование: tessera inspect-tiles INPUT [--second PATH] [--threshold 5] [--json]
        Измеряет швы A→A или A→B по горизонтали и вертикали в единицах OKLab × 100.
        Текстовый отчёт показывает среднюю и максимальную разницу и число точек выше порога.
        JSON также содержит разницу для каждого пикселя шва.
        """;
    internal const string SuggestTilesHelp = """
        Использование: tessera suggest-tiles INPUT [--margin-x 0] [--margin-y 0]
                      [--spacing-x 0] [--spacing-y 0] [--json]
        Предлагает возможные сетки атласа с тем же алгоритмом, что использует интерфейс.
        """;
    internal const string SliceTilesHelp = """
        Использование: tessera slice-tiles INPUT [--tile-width N --tile-height N | --columns N --rows N]
                      [--margin-x 0] [--margin-y 0] [--spacing-x 0] [--spacing-y 0]
                      [--output-dir DIR] [--overwrite] [--json]
        Разрезает атлас на PNG-тайлы. Требуются размеры тайла либо число столбцов и строк.
        """;
    internal const string TileSmoothHelp = """
        Использование: tessera tile-smooth INPUT [-o OUTPUT.png] [--passes 1]
                      [--edges-only] [--band 1] [--overwrite] [--json]
        Сглаживает близкие цвета тайла. --edges-only сохраняет внутреннюю область.
        """;

    public static int RunInspectTiles(string[] args, TextWriter output, TextWriter error)
    {
        if (ShowHelp(args, InspectTilesHelp, output)) return 0;
        try
        {
            var parsed = Parse(args, ["--input", "--second", "--threshold"], ["--json"]);
            double threshold = parsed.Number("--threshold", 5, 0, 100);
            using var first = Load(parsed.Input);
            string? secondPath = parsed.Value("--second");
            using var second = secondPath is null ? null : Load(Path.GetFullPath(secondPath));
            Bitmap neighbor = second ?? first;
            if (first.Size != neighbor.Size) throw new CliUsageException("Тайлы A и B должны иметь одинаковый размер.");
            byte[] a = NormalizedPixels(first), b = ReferenceEquals(neighbor, first) ? a : NormalizedPixels(neighbor);
            int width = first.Width, height = first.Height;
            byte[] left = new byte[height * 4], right = new byte[height * 4];
            byte[] top = new byte[width * 4], bottom = new byte[width * 4];
            for (int y = 0; y < height; y++)
            {
                Buffer.BlockCopy(a, (y * width + width - 1) * 4, right, y * 4, 4);
                Buffer.BlockCopy(b, y * width * 4, left, y * 4, 4);
            }
            for (int x = 0; x < width; x++)
            {
                Buffer.BlockCopy(a, ((height - 1) * width + x) * 4, bottom, x * 4, 4);
                Buffer.BlockCopy(b, x * 4, top, x * 4, 4);
            }
            TileSeamAnalysis analysis = TileSeamAnalyzer.AnalyzeEdges(right, left, bottom, top);
            SeamSummary horizontal = TileSeamAnalysis.Summarize(analysis.Horizontal, threshold);
            SeamSummary vertical = TileSeamAnalysis.Summarize(analysis.Vertical, threshold);
            if (parsed.Flag("--json")) output.WriteLine(Json(new
            {
                process = "inspect-tiles", input = parsed.Input, second = secondPath is null ? parsed.Input : Path.GetFullPath(secondPath),
                size = new { width, height }, threshold, horizontal, vertical,
                horizontalDifferences = analysis.Horizontal, verticalDifferences = analysis.Vertical
            }));
            else
            {
                output.WriteLine($"Горизонтальный шов: среднее {horizontal.Mean:F2}, максимум {horizontal.Maximum:F2}, выше порога {horizontal.AboveThreshold}/{horizontal.Count}");
                output.WriteLine($"Вертикальный шов: среднее {vertical.Mean:F2}, максимум {vertical.Maximum:F2}, выше порога {vertical.AboveThreshold}/{vertical.Count}");
            }
            return 0;
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(BaseMessage(ex)); return 1; }
    }

    public static int RunSuggestTiles(string[] args, TextWriter output, TextWriter error)
    {
        if (ShowHelp(args, SuggestTilesHelp, output)) return 0;
        try
        {
            var parsed = Parse(args, ["--input", "--margin-x", "--margin-y", "--spacing-x", "--spacing-y"], ["--json"]);
            var (marginX, marginY, spacingX, spacingY) = TileOffsets(parsed);
            using var source = Load(parsed.Input);
            IReadOnlyList<TileSetSuggestion> suggestions = TileSetSlicer.Suggest(NormalizedPixels(source), source.Width, source.Height,
                marginX, marginY, spacingX, spacingY);
            if (parsed.Flag("--json")) output.WriteLine(Json(new { process = "suggest-tiles", input = parsed.Input, suggestions }));
            else foreach (var suggestion in suggestions.Take(10))
                output.WriteLine($"{suggestion.Layout.TileWidth}×{suggestion.Layout.TileHeight}: {suggestion.Layout.Columns}×{suggestion.Layout.Rows}, {suggestion.Score:F1} — {suggestion.Reason}");
            return 0;
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(BaseMessage(ex)); return 1; }
    }

    public static int RunSliceTiles(string[] args, TextWriter output, TextWriter error)
    {
        if (ShowHelp(args, SliceTilesHelp, output)) return 0;
        try
        {
            var parsed = Parse(args, ["--input", "--output-dir", "--tile-width", "--tile-height", "--columns", "--rows",
                "--margin-x", "--margin-y", "--spacing-x", "--spacing-y"], CommonFlags);
            var (marginX, marginY, spacingX, spacingY) = TileOffsets(parsed);
            bool bySize = parsed.Value("--tile-width") is not null || parsed.Value("--tile-height") is not null;
            bool byGrid = parsed.Value("--columns") is not null || parsed.Value("--rows") is not null;
            if (bySize == byGrid) throw new CliUsageException("Укажите либо размеры тайла, либо столбцы и строки.");
            if (bySize && (parsed.Value("--tile-width") is null || parsed.Value("--tile-height") is null) ||
                byGrid && (parsed.Value("--columns") is null || parsed.Value("--rows") is null))
                throw new CliUsageException("Для сетки нужны оба значения ширины и высоты либо столбцов и строк.");
            using var source = Load(parsed.Input);
            TileSetLayout layout;
            try
            {
                layout = bySize ? TileSetLayout.FromTileSize(source.Width, source.Height,
                    parsed.Integer("--tile-width", 0, 1, int.MaxValue), parsed.Integer("--tile-height", 0, 1, int.MaxValue),
                    marginX, marginY, spacingX, spacingY)
                    : TileSetLayout.FromGrid(source.Width, source.Height,
                    parsed.Integer("--columns", 0, 1, TileSetLayout.MaxTiles),
                    parsed.Integer("--rows", 0, 1, TileSetLayout.MaxTiles), marginX, marginY, spacingX, spacingY);
            }
            catch (ArgumentException ex) { throw new CliUsageException(ex.Message); }
            string directory = Path.GetFullPath(parsed.Value("--output-dir") ?? Path.Combine(Path.GetDirectoryName(parsed.Input)!,
                Path.GetFileNameWithoutExtension(parsed.Input) + "_tiles"));
            IReadOnlyList<TileSetRegion> regions = TileSetSlicer.GetRegions(source.Width, source.Height, layout);
            string[] targets = regions.Select(region => Path.Combine(directory,
                $"{Path.GetFileNameWithoutExtension(parsed.Input)}_c{region.Column + 1}_r{region.Row + 1}.png")).ToArray();
            ValidateOutputs(parsed.Input, parsed.Flag("--overwrite"), targets);
            for (int q = 0; q < regions.Count; q++)
            {
                TileSetRegion region = regions[q];
                using var tile = source.Clone(new Rectangle(region.X, region.Y, region.Width, region.Height), PixelFormat.Format32bppArgb);
                CliApplication.SaveImage(tile, targets[q], parsed.Flag("--overwrite"));
            }
            if (parsed.Flag("--json")) output.WriteLine(Json(new { process = "slice-tiles", input = parsed.Input, layout, outputs = targets }));
            else output.WriteLine($"Сохранено тайлов: {targets.Length}; папка: {directory}");
            return 0;
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(BaseMessage(ex)); return 1; }
    }

    public static int RunTileSmooth(string[] args, TextWriter output, TextWriter error)
        => RunSingle(args, TileSmoothHelp, "_tile_smoothed.png", "tile-smooth",
            ["--input", "--output", "--passes", "--band"], (parsed, source) =>
            {
                int passes = parsed.Integer("--passes", 1, 1, DownscaleOptions.MaxLocalColorPasses);
                int band = parsed.Integer("--band", 1, 1, int.MaxValue);
                using var processed = IndependentImageProcessor.ApplyNeighborColors(source,
                    new DownscaleOptions { LocalColorPasses = passes });
                if (!parsed.Flag("--edges-only")) return (Bitmap)processed.Clone();
                byte[] original = NormalizedPixels(source), output = NormalizedPixels(processed);
                if (source.Width > 2 * (long)band)
                    for (int y = band; y < source.Height - band; y++)
                        Buffer.BlockCopy(original, (y * source.Width + band) * 4,
                            output, (y * source.Width + band) * 4, (source.Width - band * 2) * 4);
                return FromPixels(output, source.Width, source.Height);
            }, output, error, ["--edges-only", "--overwrite", "--json"]);

    private static (int MarginX, int MarginY, int SpacingX, int SpacingY) TileOffsets(Arguments parsed)
        => (parsed.Integer("--margin-x", 0, 0, int.MaxValue), parsed.Integer("--margin-y", 0, 0, int.MaxValue),
            parsed.Integer("--spacing-x", 0, 0, int.MaxValue), parsed.Integer("--spacing-y", 0, 0, int.MaxValue));

    private static byte[] NormalizedPixels(Bitmap source)
    {
        using var copy = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        return Pixels(copy);
    }
}
