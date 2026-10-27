using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PixelArtDownscale;

namespace Pixelizator.Cli;

internal static class ConnectivityPaletteCommand
{
    internal const string Help = """
        Tessera connected-palette — экспериментальное сокращение палитры с учётом связности.
        Использование: tessera connected-palette input.png [-o result.png] [параметры]

          -i, --input PATH             Исходное изображение.
          -o, --output PATH            PNG; по умолчанию <имя>_connected-palette.png.
          --colors N                   Целевое число видимых RGB-цветов: 1–4096 (128).
          --connectivity N             Вес связности областей: 0–100% (70).
          --tile-size N                Масштаб локального анализа: 0 или 4–256 px (32); 0 отключает.
          --local-weight N             Вес пространственной концентрации: 0–100% (40).
          --detail-weight N            Защита цветовых различий: 0–100% (50).
          --context-weight N           Влияние соседних цветов на замену: 0–100% (30).
          --space oklab|lab            Цветовое пространство расстояния (oklab).
          --overwrite                 Разрешить замену существующего PNG.
          --json                      Отчёт JSON.
          -h, --help                   Эта справка.

        Входная палитра: не более 4096 RGB. Для более богатой палитры сначала
        примените Median Cut или K-means. Размер и альфа каждого пикселя сохраняются;
        новые цвета и дизеринг не добавляются.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"]) { output.WriteLine(Help); return 0; }
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
                if (name is "--overwrite" or "--json")
                {
                    if (equals >= 0) throw new CliUsageException($"{name} не принимает значение.");
                    Add(name, "true");
                }
                else if (name is "--input" or "--output" or "--colors" or "--connectivity" or "--tile-size" or
                         "--local-weight" or "--detail-weight" or "--context-weight" or "--space")
                {
                    if (equals >= 0) Add(name, token[(equals + 1)..]);
                    else if (q + 1 < args.Length && !args[q + 1].StartsWith('-')) Add(name, args[++q]);
                    else throw new CliUsageException($"Для {name} требуется значение.");
                }
                else throw new CliUsageException($"Неизвестный параметр: {name}.");
            }
            if (!values.TryGetValue("--input", out string? input)) throw new CliUsageException("Укажите изображение.");
            int tileSize = Integer("--tile-size", 32, 0, 256);
            if (tileSize is > 0 and < 4) throw new CliUsageException("--tile-size: 0 или от 4 до 256.");
            string space = values.GetValueOrDefault("--space", "oklab");
            var options = new ConnectivityPaletteOptions
            {
                TargetColors = Integer("--colors", 128, 1, 4096),
                ConnectivityWeight = Percent("--connectivity", 70),
                TileSize = tileSize,
                LocalWeight = Percent("--local-weight", 40),
                DetailWeight = Percent("--detail-weight", 50),
                ContextWeight = Percent("--context-weight", 30),
                DistanceSpace = space switch
                {
                    "oklab" => PaletteDistanceSpace.Oklab,
                    "lab" => PaletteDistanceSpace.CieLab,
                    _ => throw new CliUsageException("--space: oklab или lab.")
                }
            };

            input = Path.GetFullPath(input);
            string resultPath = Path.GetFullPath(values.GetValueOrDefault("--output",
                Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + "_connected-palette.png")));
            bool overwrite = values.ContainsKey("--overwrite");
            if (!File.Exists(input)) throw new IOException($"Исходный файл не найден: {input}");
            if (!Path.GetExtension(resultPath).Equals(".png", StringComparison.OrdinalIgnoreCase))
                throw new CliUsageException("Результат нужно сохранить в PNG.");
            if (string.Equals(input, resultPath, StringComparison.OrdinalIgnoreCase))
                throw new CliUsageException("Исходник нельзя перезаписывать.");
            if (File.Exists(resultPath) && !overwrite)
                throw new IOException("Результат уже существует. Укажите --overwrite или другой путь.");

            using var source = CliApplication.LoadImage(input);
            var timer = Stopwatch.StartNew();
            var report = ConnectivityPaletteReducer.Process(source, options);
            using (report.Image) CliApplication.SaveImage(report.Image, resultPath, overwrite);
            timer.Stop();
            if (values.ContainsKey("--json"))
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    process = "connected-palette", input, output = resultPath, source.Width, source.Height,
                    report.ColorsBefore, report.ColorsAfter, report.ComponentsBefore,
                    report.RemovedColors, report.ChangedPixels,
                    settings = new { options.TargetColors, options.ConnectivityWeight, options.TileSize,
                        options.LocalWeight, options.DetailWeight, options.ContextWeight, space },
                    elapsedSeconds = timer.Elapsed.TotalSeconds
                }, new JsonSerializerOptions { WriteIndented = true }));
            else output.WriteLine($"Связная палитра: {resultPath}; цветов {report.ColorsBefore} → {report.ColorsAfter}; " +
                $"изменено {report.ChangedPixels} пикселей.");
            return 0;

            void Add(string name, string value)
            {
                if (string.IsNullOrWhiteSpace(value)) throw new CliUsageException($"Пустое значение {name}.");
                if (!values.TryAdd(name, value)) throw new CliUsageException($"Параметр {name} указан дважды.");
            }
            int Integer(string name, int fallback, int min, int max)
            {
                if (!values.TryGetValue(name, out string? raw)) return fallback;
                if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed < min || parsed > max)
                    throw new CliUsageException($"{name}: число от {min} до {max}.");
                return parsed;
            }
            double Percent(string name, int fallback)
            {
                if (!values.TryGetValue(name, out string? raw)) return fallback / 100.0;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
                    !double.IsFinite(parsed) || parsed is < 0 or > 100)
                    throw new CliUsageException($"{name}: число от 0 до 100.");
                return parsed / 100.0;
            }
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (ArgumentException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(ex.GetBaseException().Message); return 1; }
    }
}
