using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PixelArtDownscale;

namespace Pixelizator.Cli;

internal static class StructureProbeCommand
{
    internal const string Help = """
        Tessera structure-probe — экспериментальная очистка малых цветовых областей.
        Использование: tessera structure-probe input.png [-o result.png] [параметры]

          -i, --input PATH                  Исходное изображение.
          -o, --output PATH                 PNG; по умолчанию <имя>_structure.png.
          --map-output PATH                 Дополнительно сохранить карту кандидатов PNG.
          --map-only                       Основным результатом сделать карту кандидатов.
          --similar-color N                Сходство соседних цветов в LAB: 0–100 (12).
          --max-component N                Максимальный размер области: 1–4096 (8).
          --neighbor-agreement N           Минимальная доля цвета замены на границе: 0–100% (70).
          --max-replacement-distance N     Максимальное расстояние до цвета замены: 1–150 (60).
          --minimum-confidence N           Порог применения замены: 0–100% (65).
          --detail-distance N              Беречь области до 2 px при LAB-различии от N: 0–100 (25).
          --allow-alpha-edge               Разрешить изменения около прозрачности.
          --allow-lines                    Разрешить изменения тонких линий.
          --overwrite                      Разрешить замену существующих результатов.
          --json                           Отчёт JSON.
          -h, --help                        Эта справка.

        Размер и альфа каждого пикселя сохраняются. Карта показывает потенциальные
        замены красным; интенсивность соответствует уверенности. Компоненты
        связываются также по диагонали. Инструмент экспериментальный: проверяйте
        маленькие детали перед дальнейшим сжатием сеткой.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"]) { output.WriteLine(Help); return 0; }
        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            bool positional = false;
            for (int i = 0; i < args.Length; i++)
            {
                string token = args[i];
                if (!positional && token == "--") { positional = true; continue; }
                if (positional || !token.StartsWith('-')) { Add("--input", token); continue; }
                int equals = token.IndexOf('=');
                string name = equals < 0 ? token : token[..equals];
                name = name switch { "-i" => "--input", "-o" => "--output", _ => name };
                if (name is "--overwrite" or "--json" or "--map-only" or "--allow-alpha-edge" or "--allow-lines")
                {
                    if (equals >= 0) throw new CliUsageException($"{name} не принимает значение.");
                    Add(name, "true");
                }
                else if (name is "--input" or "--output" or "--map-output" or "--similar-color" or
                         "--max-component" or "--neighbor-agreement" or "--max-replacement-distance" or
                         "--minimum-confidence" or "--detail-distance")
                {
                    if (equals >= 0) Add(name, token[(equals + 1)..]);
                    else if (i + 1 < args.Length && !args[i + 1].StartsWith('-')) Add(name, args[++i]);
                    else throw new CliUsageException($"Для {name} требуется значение.");
                }
                else throw new CliUsageException($"Неизвестный параметр: {name}.");
            }
            if (!values.TryGetValue("--input", out string? input)) throw new CliUsageException("Укажите изображение.");
            int component = Integer("--max-component", 8, 1, 4096);
            double similarity = Number("--similar-color", 12, 0, 100);
            double agreement = Number("--neighbor-agreement", 70, 0, 100) / 100;
            double distance = Number("--max-replacement-distance", 60, 1, 150);
            double confidence = Number("--minimum-confidence", 65, 0, 100) / 100;
            double detailDistance = Number("--detail-distance", 25, 0, 100);
            var options = new StructureProbeOptions
            {
                MaximumComponentSize = component,
                SimilarColorDistance = similarity,
                MinimumNeighborAgreement = agreement,
                MaximumReplacementDistance = distance,
                MinimumConfidence = confidence,
                DistinctDetailDistance = detailDistance,
                ProtectTransparentBoundary = !values.ContainsKey("--allow-alpha-edge"),
                ProtectLines = !values.ContainsKey("--allow-lines")
            };

            input = Path.GetFullPath(input);
            string resultPath = Path.GetFullPath(values.GetValueOrDefault("--output",
                Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + "_structure.png")));
            string? mapPath = values.TryGetValue("--map-output", out string? rawMap) ? Path.GetFullPath(rawMap) : null;
            bool overwrite = values.ContainsKey("--overwrite");
            if (!File.Exists(input)) throw new IOException($"Исходный файл не найден: {input}");
            ValidatePath(resultPath);
            if (mapPath is not null) ValidatePath(mapPath);
            if (string.Equals(input, resultPath, StringComparison.OrdinalIgnoreCase) ||
                (mapPath is not null && (string.Equals(input, mapPath, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(resultPath, mapPath, StringComparison.OrdinalIgnoreCase))))
                throw new CliUsageException("Пути исходника и результатов должны различаться.");
            if ((File.Exists(resultPath) || mapPath is not null && File.Exists(mapPath)) && !overwrite)
                throw new IOException("Результат уже существует. Укажите --overwrite или другой путь.");

            using var source = CliApplication.LoadImage(input);
            var timer = Stopwatch.StartNew();
            var analysis = StructureProbe.Process(source, options);
            using (analysis.Image)
            using (analysis.Heatmap)
            {
                bool mapOnly = values.ContainsKey("--map-only");
                CliApplication.SaveImage(mapOnly ? analysis.Heatmap : analysis.Image, resultPath, overwrite);
                if (mapPath is not null)
                    CliApplication.SaveImage(analysis.Heatmap, mapPath, overwrite);
            }
            timer.Stop();
            if (values.ContainsKey("--json"))
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    process = "structure-probe", input, output = resultPath, mapOutput = mapPath,
                    mode = values.ContainsKey("--map-only") ? "map" : "image",
                    width = source.Width, height = source.Height, analysis.ComponentCount,
                    analysis.CandidateComponents, analysis.CandidatePixels, analysis.ChangedPixels,
                    elapsedSeconds = timer.Elapsed.TotalSeconds
                }, new JsonSerializerOptions { WriteIndented = true }));
            else
                output.WriteLine($"Проба структуры: {resultPath}; компонентов {analysis.ComponentCount}; " +
                    $"кандидатов {analysis.CandidatePixels}; замен {analysis.ChangedPixels}.");
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
            double Number(string name, double fallback, double min, double max)
            {
                if (!values.TryGetValue(name, out string? raw)) return fallback;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
                    !double.IsFinite(parsed) || parsed < min || parsed > max)
                    throw new CliUsageException($"{name}: число от {min} до {max}.");
                return parsed;
            }
            static void ValidatePath(string path)
            {
                if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
                    throw new CliUsageException("Результат нужно сохранить в PNG.");
                if (Directory.Exists(path)) throw new IOException("Вместо файла указана папка.");
            }
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (ArgumentException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(ex.GetBaseException().Message); return 1; }
    }
}
