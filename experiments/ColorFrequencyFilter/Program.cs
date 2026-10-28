using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text.Json;

namespace ColorFrequencyFilter;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            Console.WriteLine("""
                Экспериментальная очистка редких RGB-цветов.
                frequency-filter INPUT.png -o OUTPUT.png --threshold-percent 0.01 [--json] [--overwrite]
                  --threshold-percent N  Заменить цвета с долей строго ниже N% видимых пикселей (0–100).
                  --json                 Вывести статистику в JSON.
                  --overwrite            Разрешить замену существующего результата.
                Полностью прозрачные пиксели не участвуют в подсчёте; альфа всех пикселей сохраняется.
                Ближайший оставшийся цвет выбирается по расстоянию CIE76 Lab.
                """);
            return 0;
        }

        try
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool overwrite = false, json = false;
            for (int q = 0; q < args.Length; q++)
            {
                string token = args[q];
                if (token == "--overwrite") { overwrite = true; continue; }
                if (token == "--json") { json = true; continue; }
                string name = token switch { "-i" => "--input", "-o" => "--output", _ => token };
                if (!token.StartsWith('-')) { Add("--input", token); continue; }
                if (name is not ("--input" or "--output" or "--threshold-percent"))
                    throw new ArgumentException($"Неизвестный параметр: {name}");
                if (++q == args.Length) throw new ArgumentException($"Отсутствует значение: {name}");
                Add(name, args[q]);
            }
            if (!values.TryGetValue("--input", out string? sourcePath)) throw new ArgumentException("Укажите исходник.");
            if (!values.TryGetValue("--threshold-percent", out string? thresholdText))
                throw new ArgumentException("Укажите --threshold-percent.");
            if (!double.TryParse(thresholdText.Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double thresholdPercent) || !double.IsFinite(thresholdPercent) ||
                thresholdPercent is < 0 or > 100)
                throw new ArgumentException("Порог должен быть числом от 0 до 100%.");

            string input = Path.GetFullPath(sourcePath);
            string suffix = thresholdPercent.ToString("0.################", CultureInfo.InvariantCulture).Replace('.', '_');
            string target = Path.GetFullPath(values.GetValueOrDefault("--output",
                Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + $"_frequency-{suffix}.png")));
            if (input.Equals(target, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Исходник нельзя перезаписывать.");
            if (!Path.GetExtension(target).Equals(".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Результат должен быть PNG.");
            if (!File.Exists(input)) throw new FileNotFoundException("Исходник не найден.", input);
            if (File.Exists(target) && !overwrite) throw new IOException("Результат существует; используйте --overwrite.");

            using var source = new Bitmap(input);
            var timer = Stopwatch.StartNew();
            var processing = FrequencyColorProcessor.Process(source, thresholdPercent);
            using var result = processing.Image;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var stream = new FileStream(target, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write))
                result.Save(stream, ImageFormat.Png);

            var report = new { process = "experimental-frequency-filter", input, output = target,
                thresholdPercent, width = result.Width, height = result.Height, processing.VisiblePixels,
                processing.ColorsBefore, processing.ColorsAfter, processing.ReplacedColors,
                processing.ChangedPixels, processing.MaximumDeltaLab, elapsedSeconds = timer.Elapsed.TotalSeconds };
            Console.WriteLine(json ? JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })
                : $"Сохранено: {target}; цветов {processing.ColorsBefore} → {processing.ColorsAfter}; " +
                  $"заменено {processing.ChangedPixels} пикселей.");
            return 0;

            void Add(string name, string value)
            {
                if (!values.TryAdd(name, value)) throw new ArgumentException($"Параметр повторяется: {name}");
            }
        }
        catch (ArgumentException exception) { Console.Error.WriteLine(exception.Message); return 2; }
        catch (Exception exception) { Console.Error.WriteLine(exception.Message); return 1; }
    }
}
