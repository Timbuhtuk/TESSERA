using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PixelArtDownscale;

namespace Pixelizator.Cli;

internal static class ImageToolsCommand
{
    internal const string MonochromeHelp = """
        Tessera monochrome — перевести видимые пиксели в оттенки серого.
        Использование: tessera monochrome input.png [-o output.png]

          -i, --input PATH       Исходное изображение.
          -o, --output PATH      PNG; по умолчанию <имя>_monochrome.png.
          --overwrite            Разрешить замену результата.
          --json                 Отчёт JSON.
          -h, --help             Эта справка.

        Размер и альфа сохраняются. Команда mono является псевдонимом.
        """;

    internal const string SmoothHelp = """
        Tessera smooth-colors — очистить близкие оттенки, сохраняя детали и края.
        Использование: tessera smooth-colors input.png [-o output.png] [параметры]

          -i, --input PATH       Исходное изображение.
          -o, --output PATH      PNG; по умолчанию <имя>_smoothed.png.
          --passes N             Проходы 3×3: 1–100 (1).
          --color-tolerance N    Сходство цветов: 0–40 (12).
          --strength N           Сила очистки: 0–100 (65).
          --detail-protection N  Защита мелких деталей: 0–100 (80).
          --edge-protection N    Защита краёв: 0–100 (80).
          --overwrite            Разрешить замену результата.
          --json                 Отчёт JSON.
          -h, --help             Эта справка.

        Выбираются только цвета видимых соседей; размер и альфа сохраняются.
        Команда smooth является псевдонимом.
        """;

    internal const string ContourHelp = """
        Tessera clean-contours — найти градиентных кандидатов и осторожно исправить часть из них.
        Использование: tessera clean-contours input.png [-o output.png] [параметры]

          -i, --input PATH          Изображение после предварительной очистки.
          -o, --output PATH         PNG; по умолчанию <имя>_contours.png.
          --window-size N           Размер области анализа: 3, 5, 7, 9, 11, 13 или 15 (7).
          --similar-color-distance N Допуск похожих цветов в LAB: 0–100 (24).
          --min-pixel-gradient N    Минимальный градиент пикселя в ΔE²: 0–20000 (120).
          --min-neighbor-gradient N Минимальный градиент соседа в ΔE²: 0–20000 (80).
          --min-similar-neighbors N Минимум похожих соседей: 1–224 (2).
          --min-agreement N         Согласие направлений соседей: 0–100% (35).
          --min-opposition N        Противоположность градиентов: 0–100% (10).
          --strictness VALUE        Строгость последующей замены: cautious, normal, strong (cautious).
          --no-detail-guard         Отключить защиту тонких линий и перемычек.
          --candidates-output PATH  PNG-карта предложенных изменений.
          --overwrite              Разрешить замену существующих результатов.
          --json                   Отчёт JSON.
          -h, --help                Эта справка.

        Кандидат имеет сильный градиент против общего градиента соседей похожего цвета.
        Кандидат может остаться без замены, если модель границы не уверена. Размер и альфа сохраняются.
        Для замены используются только цвета исходного изображения.
        """;

    internal const string ReplaceHelp = """
        Tessera replace-color — заменить один RGB-цвет во всех видимых пикселях.
        Использование: tessera replace-color input.png -o output.png --from #112233 --to #AABBCC

          -i, --input PATH       Исходное изображение.
          -o, --output PATH      PNG; по умолчанию <имя>_recolored.png.
          --from #RRGGBB         Заменяемый цвет; обязателен.
          --to #RRGGBB           Новый цвет; обязателен.
          --overwrite            Разрешить замену результата.
          --json                 Отчёт JSON.
          -h, --help             Эта справка.

        Прозрачные пиксели не меняются; альфа видимых пикселей сохраняется.
        """;

    internal const string PaletteHelp = """
        Tessera palette-info — показать число и список видимых RGB-цветов.
        Использование: tessera palette-info input.png [--json]

          -i, --input PATH       Исходное изображение.
          --json                 Отчёт JSON с массивом colors.
          -h, --help             Эта справка.

        Список выводится максимум для 1024 цветов; общее число считается всегда.
        """;

    internal const string AsciiHelp = """
        Tessera ascii — преобразовать изображение в ASCII-текст и/или PNG.
        Использование: tessera ascii input.png [-o output.txt] [параметры]

          -i, --input PATH       Исходное изображение.
          -o, --output PATH      Один результат .txt или .png.
          --text-output PATH     Сохранить ASCII-текст.
          --image-output PATH    Сохранить отрисованный PNG.
          --mode VALUE           fit или per-pixel (fit).
          --preset VALUE         standard, detailed, minimal, blocks (standard).
          --characters TEXT      Собственная шкала яркости в одну строку.
          --invert-source        Инвертировать яркость при выборе символов.
          --invert-render        Чёрный текст на белом фоне вместо белого на чёрном.
          --overwrite            Разрешить замену результатов.
          --json                 Отчёт JSON.
          -h, --help             Эта справка.

        fit подбирает сетку символов и сохраняет размер исходника у PNG.
        per-pixel создаёт один символ на каждый пиксель. Без путей сохраняется
        <имя>_ascii.txt. --output нельзя сочетать с отдельными путями результатов.
        """;

    private static readonly HashSet<string> CommonFlags = new(StringComparer.Ordinal)
    {
        "--overwrite", "--json"
    };

    public static int RunMonochrome(string[] args, TextWriter output, TextWriter error)
    {
        if (ShowHelp(args, MonochromeHelp, output)) return 0;
        try
        {
            var parsed = Parse(args, ["--input", "--output"], CommonFlags);
            string target = GetImageOutput(parsed, "_monochrome.png");
            using var source = Load(parsed.Input);
            var timer = Stopwatch.StartNew();
            using var result = ImageFilters.Monochrome(source);
            CliApplication.SaveImage(result, target, parsed.Flag("--overwrite"));
            timer.Stop();
            WriteImageReport("monochrome", parsed, target, source, result, timer, output);
            return 0;
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(BaseMessage(ex)); return 1; }
    }

    public static int RunSmooth(string[] args, TextWriter output, TextWriter error)
    {
        if (ShowHelp(args, SmoothHelp, output)) return 0;
        try
        {
            var parsed = Parse(args,
                ["--input", "--output", "--passes", "--color-tolerance", "--strength", "--detail-protection", "--edge-protection"], CommonFlags);
            string target = GetImageOutput(parsed, "_smoothed.png");
            var defaults = new ColorCleanupOptions();
            var options = new DownscaleOptions
            {
                LocalColorPasses = parsed.Integer("--passes", 1, 1, DownscaleOptions.MaxLocalColorPasses),
                Cleanup = new ColorCleanupOptions
                {
                    ColorTolerance = parsed.Number("--color-tolerance", defaults.ColorTolerance, 0, 40),
                    Strength = parsed.Number("--strength", defaults.Strength * 100, 0, 100) / 100,
                    DetailProtection = parsed.Number("--detail-protection", defaults.DetailProtection * 100, 0, 100) / 100,
                    EdgeProtection = parsed.Number("--edge-protection", defaults.EdgeProtection * 100, 0, 100) / 100
                }
            };
            using var source = Load(parsed.Input);
            var timer = Stopwatch.StartNew();
            using var result = IndependentImageProcessor.ApplyNeighborColors(source, options);
            CliApplication.SaveImage(result, target, parsed.Flag("--overwrite"));
            timer.Stop();
            if (parsed.Flag("--json"))
                output.WriteLine(Json(new
                {
                    process = "smooth-colors", input = parsed.Input, output = target,
                    size = new { width = result.Width, height = result.Height }, options.LocalColorPasses,
                    options.Cleanup, elapsedSeconds = timer.Elapsed.TotalSeconds
                }));
            else output.WriteLine($"Сохранено: {target} ({result.Width} × {result.Height}); {Seconds(timer)} с.");
            return 0;
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(BaseMessage(ex)); return 1; }
    }

    public static int RunContour(string[] args, TextWriter output, TextWriter error)
    {
        if (ShowHelp(args, ContourHelp, output)) return 0;
        try
        {
            var parsed = Parse(args, ["--input", "--output", "--window-size", "--strictness", "--candidates-output",
                    "--similar-color-distance", "--min-pixel-gradient", "--min-neighbor-gradient",
                    "--min-similar-neighbors", "--min-agreement", "--min-opposition"],
                ["--overwrite", "--json", "--no-detail-guard"]);
            string target = GetImageOutput(parsed, "_contours.png");
            string? candidates = parsed.Value("--candidates-output");
            if (candidates is not null)
            {
                candidates = Path.GetFullPath(candidates);
                if (!Path.GetExtension(candidates).Equals(".png", StringComparison.OrdinalIgnoreCase))
                    throw new CliUsageException("--candidates-output должен иметь расширение .png.");
            }
            ValidateOutputs(parsed.Input, parsed.Flag("--overwrite"), target, candidates);
            int windowSize = parsed.Integer("--window-size", 7, 3, 15);
            if (windowSize % 2 == 0) throw new CliUsageException("--window-size должен быть нечётным.");
            string strictness = parsed.Choice("--strictness", "cautious", "cautious", "normal", "strong");
            var options = new ContourCleanOptions
            {
                WindowSize = windowSize,
                Strictness = strictness switch
                {
                    "normal" => ContourCleanStrictness.Normal,
                    "strong" => ContourCleanStrictness.Strong,
                    _ => ContourCleanStrictness.Cautious
                },
                ProtectDetails = !parsed.Flag("--no-detail-guard"),
                SimilarColorDistance = parsed.Integer("--similar-color-distance", 24, 0, 100),
                MinimumPixelGradient = parsed.Integer("--min-pixel-gradient", 120, 0, 20000),
                MinimumNeighborGradient = parsed.Integer("--min-neighbor-gradient", 80, 0, 20000),
                MinimumSimilarNeighbors = parsed.Integer("--min-similar-neighbors", 2, 1, 224),
                MinimumAgreement = parsed.Integer("--min-agreement", 35, 0, 100) / 100.0,
                MinimumOpposition = parsed.Integer("--min-opposition", 10, 0, 100) / 100.0
            };
            using var source = Load(parsed.Input);
            var timer = Stopwatch.StartNew();
            ContourCleanResult result = ContourNoiseCleaner.Process(source, options);
            using (result.Image)
            using (result.CandidateMap)
            {
                CliApplication.SaveImage(result.Image, target, parsed.Flag("--overwrite"));
                if (candidates is not null)
                    CliApplication.SaveImage(result.CandidateMap, candidates, parsed.Flag("--overwrite"));
            }
            timer.Stop();
            if (parsed.Flag("--json"))
                output.WriteLine(Json(new
                {
                    process = "clean-contours", input = parsed.Input, output = target,
                    candidatesOutput = candidates, windowSize, strictness,
                    protectDetails = options.ProtectDetails, options.SimilarColorDistance,
                    options.MinimumPixelGradient, options.MinimumNeighborGradient,
                    options.MinimumSimilarNeighbors, options.MinimumAgreement, options.MinimumOpposition,
                    result.CandidateCount, result.ChangedPixels,
                    elapsedSeconds = timer.Elapsed.TotalSeconds
                }));
            else output.WriteLine($"Сохранено: {target}; кандидатов {result.CandidateCount}, изменений {result.ChangedPixels}; {Seconds(timer)} с.");
            return 0;
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(BaseMessage(ex)); return 1; }
    }

    public static int RunReplace(string[] args, TextWriter output, TextWriter error)
    {
        if (ShowHelp(args, ReplaceHelp, output)) return 0;
        try
        {
            var parsed = Parse(args, ["--input", "--output", "--from", "--to"], CommonFlags);
            Color previous = parsed.Color("--from");
            Color replacement = parsed.Color("--to");
            string target = GetImageOutput(parsed, "_recolored.png");
            using var source = Load(parsed.Input);
            var timer = Stopwatch.StartNew();
            using var result = ImageColorTools.ReplaceColor(source, previous, replacement);
            CliApplication.SaveImage(result, target, parsed.Flag("--overwrite"));
            timer.Stop();
            if (parsed.Flag("--json"))
                output.WriteLine(Json(new
                {
                    process = "replace-color", input = parsed.Input, output = target,
                    from = Hex(previous), to = Hex(replacement),
                    size = new { width = result.Width, height = result.Height }, elapsedSeconds = timer.Elapsed.TotalSeconds
                }));
            else output.WriteLine($"Сохранено: {target}; {Hex(previous)} → {Hex(replacement)}; {Seconds(timer)} с.");
            return 0;
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(BaseMessage(ex)); return 1; }
    }

    public static int RunPalette(string[] args, TextWriter output, TextWriter error)
    {
        if (ShowHelp(args, PaletteHelp, output)) return 0;
        try
        {
            var parsed = Parse(args, ["--input"], ["--json"]);
            using var source = Load(parsed.Input);
            int count = ImageColorTools.CountColors(source);
            IReadOnlyList<Color> palette = count <= ImageColorTools.MaxPaletteColors
                ? ImageColorTools.GetPalette(source) : [];
            string[] colors = palette.Select(Hex).ToArray();
            if (parsed.Flag("--json"))
                output.WriteLine(Json(new
                {
                    process = "palette-info", input = parsed.Input,
                    size = new { width = source.Width, height = source.Height }, count,
                    colorsListed = colors.Length, colors
                }));
            else
            {
                output.WriteLine($"Видимых цветов: {count:N0}");
                if (colors.Length > 0) output.WriteLine(string.Join(Environment.NewLine, colors));
                else if (count > ImageColorTools.MaxPaletteColors)
                    output.WriteLine($"Список доступен максимум для {ImageColorTools.MaxPaletteColors} цветов.");
            }
            return 0;
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(BaseMessage(ex)); return 1; }
    }

    public static int RunAscii(string[] args, TextWriter output, TextWriter error)
    {
        if (ShowHelp(args, AsciiHelp, output)) return 0;
        try
        {
            var parsed = Parse(args,
                ["--input", "--output", "--text-output", "--image-output", "--mode", "--preset", "--characters"],
                ["--invert-source", "--invert-render", "--overwrite", "--json"]);
            string mode = parsed.Choice("--mode", "fit", "fit", "per-pixel");
            string characters = GetAsciiCharacters(parsed);
            var (textTarget, imageTarget) = GetAsciiOutputs(parsed);
            ValidateOutputs(parsed.Input, parsed.Flag("--overwrite"), textTarget, imageTarget);
            using var source = Load(parsed.Input);
            var timer = Stopwatch.StartNew();
            string text = mode == "fit"
                ? ImageFilters.ToAscii(source, characters, parsed.Flag("--invert-source"))
                : ImageFilters.ToAsciiPerPixel(source, characters, parsed.Flag("--invert-source"));
            if (textTarget is not null) SaveText(textTarget, text, parsed.Flag("--overwrite"));
            if (imageTarget is not null)
            {
                using var image = ImageFilters.RenderAscii(source, mode == "fit", characters,
                    parsed.Flag("--invert-source"), parsed.Flag("--invert-render"));
                CliApplication.SaveImage(image, imageTarget, parsed.Flag("--overwrite"));
            }
            timer.Stop();
            if (parsed.Flag("--json"))
                output.WriteLine(Json(new
                {
                    process = "ascii", input = parsed.Input, textOutput = textTarget, imageOutput = imageTarget,
                    mode, characters, invertSource = parsed.Flag("--invert-source"),
                    invertRender = parsed.Flag("--invert-render"), elapsedSeconds = timer.Elapsed.TotalSeconds
                }));
            else
            {
                if (textTarget is not null) output.WriteLine($"ASCII-текст: {textTarget}");
                if (imageTarget is not null) output.WriteLine($"ASCII-изображение: {imageTarget}");
            }
            return 0;
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(BaseMessage(ex)); return 1; }
    }

    private static bool ShowHelp(string[] args, string help, TextWriter output)
    {
        if (args.Length != 0 && args is not ["--help"] && args is not ["-h"]) return false;
        output.WriteLine(help);
        return true;
    }

    private static Arguments Parse(string[] args, IEnumerable<string> valueOptions, IEnumerable<string> flagOptions)
    {
        var valuesAllowed = valueOptions.ToHashSet(StringComparer.Ordinal);
        var flagsAllowed = flagOptions.ToHashSet(StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool positional = false;
        for (int q = 0; q < args.Length; q++)
        {
            string token = args[q];
            if (!positional && token == "--") { positional = true; continue; }
            if (positional || !token.StartsWith('-')) { Add("--input", token); continue; }
            int equals = token.IndexOf('=');
            string name = equals < 0 ? token : token[..equals];
            name = name switch { "-i" => "--input", "-o" => "--output", _ => name };
            if (flagsAllowed.Contains(name))
            {
                if (equals >= 0) throw new CliUsageException($"{name} не принимает значение.");
                Add(name, "true");
            }
            else if (valuesAllowed.Contains(name))
            {
                string value;
                if (equals >= 0) value = token[(equals + 1)..];
                else if (q + 1 < args.Length && !args[q + 1].StartsWith("--", StringComparison.Ordinal)
                         && args[q + 1] is not "-i" and not "-o" and not "-h")
                    value = args[++q];
                else throw new CliUsageException($"Для {name} требуется значение.");
                Add(name, value);
            }
            else throw new CliUsageException($"Неизвестный параметр: {name}.");
        }
        if (!values.TryGetValue("--input", out string? input))
            throw new CliUsageException("Укажите исходное изображение.");
        return new Arguments(Path.GetFullPath(input), values);

        void Add(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new CliUsageException($"Пустое значение {name}.");
            if (!values.TryAdd(name, value)) throw new CliUsageException($"Параметр {name} указан несколько раз.");
        }
    }

    private static Bitmap Load(string input)
    {
        if (!File.Exists(input)) throw new IOException($"Исходный файл не найден: {input}");
        return CliApplication.LoadImage(input);
    }

    private static string GetImageOutput(Arguments parsed, string suffix)
    {
        string target = Path.GetFullPath(parsed.Value("--output") ?? Path.Combine(Path.GetDirectoryName(parsed.Input)!,
            Path.GetFileNameWithoutExtension(parsed.Input) + suffix));
        ValidateOutputs(parsed.Input, parsed.Flag("--overwrite"), target);
        if (!Path.GetExtension(target).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException("Результат сохраняется только в PNG.");
        return target;
    }

    private static (string? Text, string? Image) GetAsciiOutputs(Arguments parsed)
    {
        string? output = parsed.Value("--output");
        if (output is not null && (parsed.Value("--text-output") is not null || parsed.Value("--image-output") is not null))
            throw new CliUsageException("--output нельзя сочетать с --text-output или --image-output.");
        string? text = parsed.Value("--text-output");
        string? image = parsed.Value("--image-output");
        if (output is not null)
        {
            string extension = Path.GetExtension(output).ToLowerInvariant();
            if (extension == ".txt") text = output;
            else if (extension == ".png") image = output;
            else throw new CliUsageException("--output для ASCII должен иметь расширение .txt или .png.");
        }
        if (text is null && image is null)
            text = Path.Combine(Path.GetDirectoryName(parsed.Input)!, Path.GetFileNameWithoutExtension(parsed.Input) + "_ascii.txt");
        text = text is null ? null : Path.GetFullPath(text);
        image = image is null ? null : Path.GetFullPath(image);
        if (text is not null && !Path.GetExtension(text).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException("--text-output должен иметь расширение .txt.");
        if (image is not null && !Path.GetExtension(image).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException("--image-output должен иметь расширение .png.");
        return (text, image);
    }

    private static string GetAsciiCharacters(Arguments parsed)
    {
        if (parsed.Value("--preset") is not null && parsed.Value("--characters") is not null)
            throw new CliUsageException("Используйте либо --preset, либо --characters.");
        string? custom = parsed.Value("--characters");
        if (custom is not null)
        {
            if (custom.Contains('\r') || custom.Contains('\n'))
                throw new CliUsageException("--characters должен содержать одну строку.");
            return custom;
        }
        string preset = parsed.Choice("--preset", "standard", "standard", "detailed", "minimal", "blocks");
        return ImageFilters.AsciiRampPresets[preset switch
        {
            "detailed" => 1,
            "minimal" => 2,
            "blocks" => 3,
            _ => 0
        }].Characters;
    }

    private static void ValidateOutputs(string input, bool overwrite, params string?[] outputs)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(input) };
        foreach (string? value in outputs)
        {
            if (value is null) continue;
            string path = Path.GetFullPath(value);
            if (!paths.Add(path)) throw new CliUsageException("Исходник и выходные файлы должны иметь разные пути.");
            if (Directory.Exists(path)) throw new IOException($"Вместо выходного файла указана папка: {path}");
            if (File.Exists(path) && !overwrite)
                throw new IOException($"Файл уже существует: {path}. Для замены добавьте --overwrite.");
        }
    }

    private static void SaveText(string path, string content, bool overwrite)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".tessera-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void WriteImageReport(string process, Arguments parsed, string target, Bitmap source, Bitmap result,
        Stopwatch timer, TextWriter output)
    {
        if (parsed.Flag("--json"))
            output.WriteLine(Json(new
            {
                process, input = parsed.Input, output = target,
                sourceSize = new { width = source.Width, height = source.Height },
                outputSize = new { width = result.Width, height = result.Height },
                elapsedSeconds = timer.Elapsed.TotalSeconds
            }));
        else output.WriteLine($"Сохранено: {target} ({result.Width} × {result.Height}); {Seconds(timer)} с.");
    }

    private static string Json(object value) => JsonSerializer.Serialize(value,
        new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    private static string Seconds(Stopwatch timer) => timer.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);
    private static string BaseMessage(Exception ex) => ex is AggregateException ? ex.GetBaseException().Message : ex.Message;
    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private sealed record Arguments(string Input, Dictionary<string, string> Values)
    {
        public string? Value(string name) => Values.GetValueOrDefault(name);
        public bool Flag(string name) => Values.ContainsKey(name);
        public int Integer(string name, int fallback, int min, int max)
        {
            if (!Values.TryGetValue(name, out string? value)) return fallback;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) || number < min || number > max)
                throw new CliUsageException($"{name}: требуется целое число от {min} до {max}.");
            return number;
        }
        public double Number(string name, double fallback, double min, double max)
        {
            if (!Values.TryGetValue(name, out string? value)) return fallback;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
                !double.IsFinite(number) || number < min || number > max)
                throw new CliUsageException($"{name}: требуется число от {min} до {max}.");
            return number;
        }
        public string Choice(string name, string fallback, params string[] choices)
        {
            if (!Values.TryGetValue(name, out string? value)) return fallback;
            string? found = choices.FirstOrDefault(choice => string.Equals(choice, value, StringComparison.OrdinalIgnoreCase));
            return found ?? throw new CliUsageException($"{name}: допустимы {string.Join(", ", choices)}.");
        }
        public Color Color(string name)
        {
            if (!Values.TryGetValue(name, out string? value)) throw new CliUsageException($"Укажите обязательный параметр {name}.");
            if (value.Length != 7 || value[0] != '#' ||
                !int.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
                throw new CliUsageException($"{name}: требуется цвет #RRGGBB.");
            return System.Drawing.Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        }
    }
}
