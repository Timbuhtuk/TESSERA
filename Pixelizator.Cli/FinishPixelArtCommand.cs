using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PixelArtDownscale;
using PixelArtAlignment;

namespace Pixelizator.Cli;

internal static class FinishPixelArtCommand
{
    internal const string Help = """
        Tessera finish-pixel-art — собрать пиксель-арт на единой сетке.
        Использование: tessera finish-pixel-art input.png -o result.png [параметры]

          -i, --input PATH          Исходный PNG.
          -o, --output PATH         Итоговый PNG; по умолчанию <имя>_pixel_art.png.
          --cell-size N             Размер исходной ячейки: от 2 (8).
          --smooth-passes N         Проходы подготовки цветов 3×3: 0–100 (1).
          --colors N                Число цветов после уменьшения: 1–4096 (128);
                                    0 оставляет исходные цвета результата.
          --rigid                   Отключить локальную подстройку сетки.
          --overwrite               Разрешить замену результата.
          --json                    Отчёт JSON.
          -h, --help                Эта справка.

        Выполнение: локальная подготовка цвета → выравнивание сетки →
        одна ячейка в один пиксель → необязательное усечение палитры.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            output.WriteLine(Help);
            return 0;
        }

        try
        {
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
                if (name is "--rigid" or "--overwrite" or "--json")
                {
                    if (equals >= 0) throw new CliUsageException($"{name} не принимает значение.");
                    Add(name, "true");
                }
                else if (name is "--input" or "--output" or "--cell-size" or "--smooth-passes" or "--colors")
                {
                    if (equals >= 0) Add(name, token[(equals + 1)..]);
                    else if (q + 1 < args.Length && !args[q + 1].StartsWith('-')) Add(name, args[++q]);
                    else throw new CliUsageException($"Для {name} требуется значение.");
                }
                else throw new CliUsageException($"Неизвестный параметр: {name}.");
            }

            if (!values.TryGetValue("--input", out string? rawInput))
                throw new CliUsageException("Укажите исходный PNG.");
            int cellSize = Number("--cell-size", 8, 2, 65535);
            int smoothPasses = Number("--smooth-passes", 1, 0, DownscaleOptions.MaxLocalColorPasses);
            int colors = Number("--colors", 128, 0, DownscaleOptions.MaxQuantizationColors);
            string input = Path.GetFullPath(rawInput);
            string target = Path.GetFullPath(values.GetValueOrDefault("--output",
                Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + "_pixel_art.png")));
            if (string.Equals(input, target, StringComparison.OrdinalIgnoreCase))
                throw new CliUsageException("Исходник нельзя перезаписывать.");
            if (!Path.GetExtension(target).Equals(".png", StringComparison.OrdinalIgnoreCase))
                throw new CliUsageException("Итоговый файл должен быть PNG.");
            if (!File.Exists(input)) throw new IOException($"Исходный файл не найден: {input}");
            if (Directory.Exists(target)) throw new IOException("Вместо выходного файла указана папка.");
            bool overwrite = values.ContainsKey("--overwrite");
            if (File.Exists(target) && !overwrite)
                throw new IOException("Результат уже существует. Укажите другое имя или --overwrite.");

            using var source = CliApplication.LoadImage(input);
            var options = new PixelArtFinishOptions
            {
                CellSize = cellSize,
                SmoothPasses = smoothPasses,
                ColorCount = colors,
                AdaptiveGrid = !values.ContainsKey("--rigid")
            };
            var timer = Stopwatch.StartNew();
            using var result = PixelArtFinisher.Process(source, options);
            CliApplication.SaveImage(result, target, overwrite);
            timer.Stop();

            if (values.ContainsKey("--json"))
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    process = "finish-pixel-art",
                    input,
                    output = target,
                    cellSize,
                    smoothPasses,
                    colors,
                    adaptiveGrid = options.AdaptiveGrid,
                    sourceSize = new { width = source.Width, height = source.Height },
                    outputSize = new { width = result.Width, height = result.Height },
                    elapsedSeconds = timer.Elapsed.TotalSeconds
                }, new JsonSerializerOptions { WriteIndented = true }));
            else output.WriteLine($"Пиксель-арт сохранён: {target}; {source.Width} × {source.Height} → " +
                                  $"{result.Width} × {result.Height}.");
            return 0;

            void Add(string name, string value)
            {
                if (string.IsNullOrWhiteSpace(value) || !values.TryAdd(name, value))
                    throw new CliUsageException($"Пустой или повторяющийся параметр: {name}.");
            }

            int Number(string name, int fallback, int minimum, int maximum)
            {
                if (!values.TryGetValue(name, out string? raw)) return fallback;
                if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ||
                    parsed < minimum || parsed > maximum)
                    throw new CliUsageException($"{name}: требуется целое число от {minimum} до {maximum}.");
                return parsed;
            }
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (ArgumentException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(ex.Message); return 1; }
    }
}
