using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Tessera.Core.Imaging;

namespace Tessera.Cli;

internal static partial class ImageToolsCommand
{
    internal const string TrimHelp = """
        Использование: tessera trim INPUT [-o OUTPUT.png] [--overwrite] [--json]
        Обрезает полностью прозрачные края. Пустой кадр даёт прозрачный 1×1 PNG.
        """;
    internal const string SharpenHelp = """
        Использование: tessera sharpen INPUT [-o OUTPUT.png] [--strength 100] [--threshold 4] [--overwrite] [--json]
        --strength 0–200; --threshold 0–64. Размер и альфа сохраняются.
        """;
    internal const string ChannelHelp = """
        Использование: tessera adjust-channel INPUT -o OUTPUT.png --channel R|G|B|A --amount N
                      [--mode value|percent] [--subtract] [--overwrite] [--json]
        N — неотрицательное конечное число. Процент вычисляется от текущего значения канала.
        """;
    internal const string TcrHelp = """
        Использование: tessera tcr INPUT [-o OUTPUT.png] [--mode t3-p44|p5-off|mixed] [--factor 8]
                      [--overwrite] [--json]
        Уменьшение TCR: фактор 2–64; режимы T3/p44, P5/OFF и 50/50.
        """;
    internal const string ContourMapHelp = """
        Использование: tessera contour-map INPUT [-o OUTPUT.png] [--window-size 7]
                      [--similar-color-distance 24] [--min-pixel-gradient 120]
                      [--min-neighbor-gradient 80] [--min-similar-neighbors 2]
                      [--min-agreement 35] [--min-opposition 10]
                      [--no-detail-guard] [--overwrite] [--json]
        Сохраняет ту же аналитическую карту контуров, что создаёт окно редактора.
        """;
    internal const string MaterialMapsHelp = """
        Использование: tessera material-maps INPUT [--output-dir DIR] [--document-output FILE.tmaps]
                      [--normal-strength 4] [--ao-strength 2] [--ao-radius 8]
                      [--normal-convention directx|opengl] [--overwrite] [--json]
        Создаёт <имя>_height.png, <имя>_normal.png, <имя>_ao.png.
        """;
    internal const string GridPreviewHelp = """
        Использование: tessera grid-preview INPUT [-o OUTPUT.png]
                      [--step-x 16] [--step-y 16] [--offset-x 0] [--offset-y 0]
                      [--color #00C8FF] [--overwrite] [--json]
        Сохраняет отдельную диагностическую PNG-копию с сеткой просмотра.
        Исходное изображение и результаты обработки не изменяются.
        """;

    public static int RunTrim(string[] args, TextWriter output, TextWriter error)
        => RunSingle(args, TrimHelp, "_trimmed.png", "trim", ["--input", "--output"],
            (parsed, source) => BitmapFactory.CropTransparentEdges(source), output, error);

    public static int RunSharpen(string[] args, TextWriter output, TextWriter error)
        => RunSingle(args, SharpenHelp, "_sharpened.png", "sharpen",
            ["--input", "--output", "--strength", "--threshold"], (parsed, source) =>
            {
                int strength = parsed.Integer("--strength", 100, 0, 200);
                int threshold = parsed.Integer("--threshold", 4, 0, 64);
                return ImageFilters.Sharpen(source, strength, threshold);
            }, output, error);

    public static int RunChannel(string[] args, TextWriter output, TextWriter error)
        => RunSingle(args, ChannelHelp, "_channel.png", "adjust-channel",
            ["--input", "--output", "--channel", "--amount", "--mode"], (parsed, source) =>
            {
                string channel = parsed.Choice("--channel", "", "r", "g", "b", "a");
                if (channel.Length == 0) throw new CliUsageException("Укажите --channel R, G, B или A.");
                if (parsed.Value("--amount") is null) throw new CliUsageException("Укажите --amount.");
                var options = new ChannelAdjustmentOptions
                {
                    Channel = channel switch { "r" => RgbaChannel.R, "g" => RgbaChannel.G, "b" => RgbaChannel.B, _ => RgbaChannel.A },
                    Mode = parsed.Choice("--mode", "value", "value", "percent") == "percent"
                        ? ChannelAdjustmentMode.Percent : ChannelAdjustmentMode.Value,
                    Amount = parsed.Number("--amount", 0, 0, double.MaxValue),
                    Subtract = parsed.Flag("--subtract")
                };
                return ChannelAdjustment.Apply(source, options);
            }, output, error, ["--subtract", "--overwrite", "--json"]);

    public static int RunTcr(string[] args, TextWriter output, TextWriter error)
        => RunSingle(args, TcrHelp, "_tcr.png", "tcr",
            ["--input", "--output", "--mode", "--factor"], (parsed, source) =>
            {
                string mode = parsed.Choice("--mode", "t3-p44", "t3-p44", "p5-off", "mixed");
                int factor = parsed.Integer("--factor", FinalistReducer.DefaultFactor,
                    FinalistReducer.MinFactor, FinalistReducer.MaxFactor);
                return FinalistReducer.Reduce(source, mode switch
                {
                    "p5-off" => FinalistMode.P5Off,
                    "mixed" => FinalistMode.HalfAndHalf,
                    _ => FinalistMode.T3P44
                }, factor);
            }, output, error);

    public static int RunContourMap(string[] args, TextWriter output, TextWriter error)
        => RunSingle(args, ContourMapHelp, "_contour_map.png", "contour-map",
            ["--input", "--output", "--window-size", "--similar-color-distance", "--min-pixel-gradient",
                "--min-neighbor-gradient", "--min-similar-neighbors", "--min-agreement", "--min-opposition"], (parsed, source) =>
            {
                int window = parsed.Integer("--window-size", 7, 3, 15);
                if (window % 2 == 0) throw new CliUsageException("--window-size должен быть нечётным.");
                var options = new ContourCleanOptions
                {
                    WindowSize = window,
                    Strictness = ContourCleanStrictness.Strong,
                    ProtectDetails = !parsed.Flag("--no-detail-guard"),
                    SimilarColorDistance = parsed.Number("--similar-color-distance", 24, 0, 100),
                    MinimumPixelGradient = parsed.Number("--min-pixel-gradient", 120, 0, 20000),
                    MinimumNeighborGradient = parsed.Number("--min-neighbor-gradient", 80, 0, 20000),
                    MinimumSimilarNeighbors = parsed.Integer("--min-similar-neighbors", 2, 1, 224),
                    MinimumAgreement = parsed.Number("--min-agreement", 35, 0, 100) / 100,
                    MinimumOpposition = parsed.Number("--min-opposition", 10, 0, 100) / 100
                };
                return ContourNoiseCleaner.Analyze(source, options).Heatmap;
            }, output, error, ["--no-detail-guard", "--overwrite", "--json"]);

    public static int RunMaterialMaps(string[] args, TextWriter output, TextWriter error)
    {
        if (ShowHelp(args, MaterialMapsHelp, output)) return 0;
        try
        {
            var parsed = Parse(args, ["--input", "--output-dir", "--document-output", "--normal-strength", "--ao-strength",
                "--ao-radius", "--normal-convention"], CommonFlags);
            string directory = Path.GetFullPath(parsed.Value("--output-dir") ?? Path.GetDirectoryName(parsed.Input)!);
            string name = Path.GetFileNameWithoutExtension(parsed.Input);
            string[] targets = new[] { "height", "normal", "ao" }
                .Select(kind => Path.Combine(directory, $"{name}_{kind}.png")).ToArray();
            string? documentPath = parsed.Value("--document-output") is { } value ? Path.GetFullPath(value) : null;
            if (documentPath is not null && !Path.GetExtension(documentPath).Equals(".tmaps", StringComparison.OrdinalIgnoreCase))
                throw new CliUsageException("--document-output должен иметь расширение .tmaps.");
            ValidateOutputs(parsed.Input, parsed.Flag("--overwrite"), [.. targets, documentPath]);
            float normalStrength = (float)parsed.Number("--normal-strength", 4, 0, 32);
            float aoStrength = (float)parsed.Number("--ao-strength", 2, 0, 16);
            int aoRadius = parsed.Integer("--ao-radius", 8, 1, 64);
            string convention = parsed.Choice("--normal-convention", "directx", "directx", "opengl");
            using var source = Load(parsed.Input);
            var timer = Stopwatch.StartNew();
            using var copy = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
            var document = new MaterialMapDocument(copy.Width, copy.Height, Pixels(copy))
            {
                NormalStrength = normalStrength, AoStrength = aoStrength, AoRadius = aoRadius,
                DirectX = convention == "directx"
            };
            document.Recalculate();
            var kinds = new[] { MaterialMapKind.Height, MaterialMapKind.Normal, MaterialMapKind.AO };
            for (int q = 0; q < kinds.Length; q++)
            {
                using var map = FromPixels(document.MapPixels(kinds[q]), document.Width, document.Height);
                CliApplication.SaveImage(map, targets[q], parsed.Flag("--overwrite"));
            }
            if (documentPath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(documentPath)!);
                MaterialMapFile.Save(document, documentPath);
            }
            timer.Stop();
            if (parsed.Flag("--json")) output.WriteLine(Json(new { process = "material-maps", input = parsed.Input,
                outputs = targets, documentOutput = documentPath, normalStrength, aoStrength, aoRadius, convention,
                elapsedSeconds = timer.Elapsed.TotalSeconds }));
            else foreach (string target in targets) output.WriteLine($"Сохранено: {target}");
            if (!parsed.Flag("--json") && documentPath is not null) output.WriteLine($"Документ карт: {documentPath}");
            return 0;
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(BaseMessage(ex)); return 1; }
    }

    public static int RunGridPreview(string[] args, TextWriter output, TextWriter error)
        => RunSingle(args, GridPreviewHelp, "_grid_preview.png", "grid-preview",
            ["--input", "--output", "--step-x", "--step-y", "--offset-x", "--offset-y", "--color"],
            (parsed, source) =>
            {
                int stepX = parsed.Integer("--step-x", 16, 1, 2048);
                int stepY = parsed.Integer("--step-y", 16, 1, 2048);
                int offsetX = parsed.Integer("--offset-x", 0, 0, stepX - 1);
                int offsetY = parsed.Integer("--offset-y", 0, 0, stepY - 1);
                Color color = parsed.Value("--color") is null ? Color.FromArgb(0, 200, 255) : parsed.Color("--color");
                var result = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
                using var graphics = Graphics.FromImage(result);
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                using var pen = new Pen(Color.FromArgb(190, color));
                for (int x = offsetX; x < result.Width; x += stepX)
                    graphics.DrawLine(pen, x, 0, x, result.Height);
                for (int y = offsetY; y < result.Height; y += stepY)
                    graphics.DrawLine(pen, 0, y, result.Width, y);
                return result;
            }, output, error);

    private static int RunSingle(string[] args, string help, string suffix, string process,
        IEnumerable<string> values, Func<Arguments, Bitmap, Bitmap> convert, TextWriter output, TextWriter error,
        IEnumerable<string>? flags = null)
    {
        if (ShowHelp(args, help, output)) return 0;
        try
        {
            var parsed = Parse(args, values, flags ?? CommonFlags);
            string target = GetImageOutput(parsed, suffix);
            using var source = Load(parsed.Input);
            var timer = Stopwatch.StartNew();
            using var result = convert(parsed, source);
            CliApplication.SaveImage(result, target, parsed.Flag("--overwrite"));
            timer.Stop();
            WriteImageReport(process, parsed, target, source, result, timer, output);
            return 0;
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(BaseMessage(ex)); return 1; }
    }

    private static byte[] Pixels(Bitmap bitmap)
    {
        var result = new byte[checked(bitmap.Width * bitmap.Height * 4)];
        var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bits = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < bitmap.Height; y++)
                Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), result, y * bitmap.Width * 4, bitmap.Width * 4);
        }
        finally { bitmap.UnlockBits(bits); }
        return result;
    }

    internal static Bitmap FromPixels(byte[] pixels, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var bounds = new Rectangle(0, 0, width, height);
        var bits = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < height; y++)
                Marshal.Copy(pixels, y * width * 4, IntPtr.Add(bits.Scan0, y * bits.Stride), width * 4);
        }
        finally { bitmap.UnlockBits(bits); }
        return bitmap;
    }
}
