using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PixelArtDownscale;

namespace Pixelizator.Cli;

internal static class IndependentCommand
{
    internal const string ScaleHelp = """
        Tessera resize — уменьшить или увеличить изображение без квантования и палитры.
        Использование: tessera resize input.png -o result.png --width 48 --height 48

          -i, --input PATH       Исходное изображение или ранее сохранённый результат.
          -o, --output PATH      PNG; по умолчанию <имя>_resized.png.
          --width N             Ширина результата: 1–3840 (48).
          --height N            Высота результата: 1–2160 (48).
          --sprite              Весь кадр без обрезки; порог альфы действует при уменьшении.
          --alpha-threshold N   Порог покрытия: 1–100% (50), только --sprite при уменьшении.
          --crop-horizontal V   center, left, right (center).
          --crop-vertical V     center, top, bottom (center).
          --block-mode V        automatic или manual (automatic).
          --brightness N        0–100 (50), только manual.
          --contrast N          0–100 (50), только manual.
          --saturation N        0–100 (50), только manual.
          --edge N              0–100 (50), только manual.
          --overwrite           Разрешить замену результата.
          --json                Отчёт JSON.
          -h, --help            Эта справка.

        Выбирается цвет исходного пикселя; новые цвета не создаются. Увеличение — без сглаживания.
        Для обработки уже готового результата передайте путь к нему как input.
        """;

    internal const string ColorsHelp = """
        Tessera colors — применить цвета без изменения размера и альфы.
        Использование: tessera colors input.png -o result.png --palette db16

          -i, --input PATH          Исходное изображение или ранее сохранённый результат.
          -o, --output PATH         PNG; по умолчанию <имя>_colors.png.
          --palette V              none, db16, db32, nes, gameboy, step (db16).
          --palette-step N          1–255 (32), только --palette step.
          --quantization V          median-cut, kmeans-lab, kmeans-linear (kmeans-lab).
          --quantization-colors N   1–4096 (64).
          --color-weights           Учитывать частоту исходных цветов.
          --dithering               Дизеринг для DB16, DB32, NES и GameBoy.
          --overwrite               Разрешить замену результата.
          --json                    Отчёт JSON.
          -h, --help                Эта справка.

        Для обработки уже готового результата передайте путь к нему как input.
        """;

    private static readonly HashSet<string> ScaleOptions = new(StringComparer.Ordinal)
    {
        "--input", "--output", "--width", "--height", "--sprite", "--alpha-threshold",
        "--crop-horizontal", "--crop-vertical", "--block-mode", "--brightness", "--contrast",
        "--saturation", "--edge", "--overwrite", "--json"
    };

    private static readonly HashSet<string> ColorOptions = new(StringComparer.Ordinal)
    {
        "--input", "--output", "--palette", "--palette-step", "--quantization",
        "--quantization-colors", "--color-weights", "--dithering", "--overwrite", "--json"
    };

    public static int Run(string[] args, bool scale, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            output.WriteLine(scale ? ScaleHelp : ColorsHelp);
            return 0;
        }

        try
        {
            ValidateOptions(args, scale);
            CliArguments parsed;
            try { parsed = CliArguments.Parse(args); }
            catch (ArgumentException ex) { throw new CliUsageException(ex.Message); }

            bool explicitOutput = args.Any(a => a is "-o" or "--output" || a.StartsWith("--output=", StringComparison.Ordinal) ||
                a.StartsWith("-o=", StringComparison.Ordinal));
            string target = explicitOutput ? parsed.Output : Path.Combine(Path.GetDirectoryName(parsed.Input)!,
                Path.GetFileNameWithoutExtension(parsed.Input) + (scale ? "_resized.png" : "_colors.png"));
            if (string.Equals(parsed.Input, target, StringComparison.OrdinalIgnoreCase))
                throw new CliUsageException("Исходник нельзя перезаписывать.");
            if (!Path.GetExtension(target).Equals(".png", StringComparison.OrdinalIgnoreCase))
                throw new CliUsageException("Эта операция сохраняет результат только в PNG для сохранения альфы.");
            if (!File.Exists(parsed.Input)) throw new IOException($"Исходный файл не найден: {parsed.Input}");
            if (Directory.Exists(target)) throw new IOException("Вместо выходного файла указана папка.");
            if (File.Exists(target) && !parsed.Overwrite)
                throw new IOException("Результат уже существует. Укажите другое имя или --overwrite.");

            using var source = CliApplication.LoadImage(parsed.Input);

            var timer = Stopwatch.StartNew();
            using var result = scale
                ? IndependentImageProcessor.Scale(source, parsed.Options)
                : IndependentImageProcessor.ApplyColors(source, parsed.Options);
            CliApplication.SaveImage(result, target, parsed.Overwrite);
            timer.Stop();

            if (parsed.Json)
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    process = scale ? "resize-only" : "colors-only",
                    input = parsed.Input,
                    output = target,
                    sourceSize = new { width = source.Width, height = source.Height },
                    outputSize = new { width = result.Width, height = result.Height },
                    options = parsed.Options,
                    elapsedSeconds = timer.Elapsed.TotalSeconds
                }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else
                output.WriteLine($"Сохранено: {target} ({result.Width} × {result.Height}); " +
                    $"{timer.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)} с.");
            return 0;
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(ex is AggregateException ? ex.GetBaseException().Message : ex.Message); return 1; }
    }

    private static void ValidateOptions(string[] args, bool scale)
    {
        bool positional = false;
        foreach (string token in args)
        {
            if (!positional && token == "--") { positional = true; continue; }
            if (positional || !token.StartsWith('-')) continue;
            string name = token.Split('=', 2)[0] switch
            {
                "-i" => "--input",
                "-o" => "--output",
                var value => value
            };
            if (!(scale ? ScaleOptions : ColorOptions).Contains(name))
                throw new CliUsageException($"Параметр {name} недоступен для команды {(scale ? "resize" : "colors")}.");
        }
    }
}
