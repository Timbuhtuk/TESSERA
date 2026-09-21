using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PixelArtAlignment;

namespace Pixelizator.Cli;

internal static class GridReductionCommand
{
    internal const string Help = """
        Tessera reduce-grid — превратить каждую ячейку выровненной сетки в один пиксель.
        Использование: tessera reduce-grid aligned.png --cell-size 4 [-o pixels.png]

          -i, --input PATH       Выровненное изображение или сохранённый результат.
          -o, --output PATH      PNG; по умолчанию <имя>_pixels.png.
          --cell-size N          Размер ячейки в исходных пикселях: от 2; обязателен.
          --overwrite            Разрешить замену результата.
          --json                 Отчёт JSON.
          -h, --help             Эта справка.

        Размер ячейки берите из отчёта tessera align. Цвета и альфа копируются
        без квантования; неоднородная сетка отклоняется.
        Команда compact-grid является псевдонимом.
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
                if (name is "--overwrite" or "--json")
                {
                    if (equals >= 0) throw new CliUsageException($"{name} не принимает значение.");
                    Add(name, "true");
                }
                else if (name is "--input" or "--output" or "--cell-size")
                {
                    if (equals >= 0) Add(name, token[(equals + 1)..]);
                    else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                             && args[i + 1] is not "-i" and not "-o" and not "-h")
                        Add(name, args[++i]);
                    else throw new CliUsageException($"Для {name} требуется значение.");
                }
                else throw new CliUsageException($"Неизвестный параметр сжатия сетки: {name}.");
            }

            if (!values.TryGetValue("--input", out string? input)) throw new CliUsageException("Укажите выровненное изображение.");
            if (!values.TryGetValue("--cell-size", out string? rawSize) ||
                !int.TryParse(rawSize, NumberStyles.None, CultureInfo.InvariantCulture, out int cellSize) ||
                cellSize < 2 || cellSize > 65535)
                throw new CliUsageException("--cell-size: требуется целое число от 2 до 65535.");

            input = Path.GetFullPath(input);
            string target = Path.GetFullPath(values.GetValueOrDefault("--output",
                Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + "_pixels.png")));
            if (string.Equals(input, target, StringComparison.OrdinalIgnoreCase))
                throw new CliUsageException("Исходник нельзя перезаписывать.");
            if (!Path.GetExtension(target).Equals(".png", StringComparison.OrdinalIgnoreCase))
                throw new CliUsageException("Результат сжатия сетки нужно сохранить в PNG.");
            if (!File.Exists(input)) throw new IOException($"Исходный файл не найден: {input}");
            bool overwrite = values.ContainsKey("--overwrite");
            using var source = CliApplication.LoadImage(input);
            if (cellSize > Math.Min(source.Width, source.Height))
                throw new CliUsageException("Ячейка больше изображения.");
            if (Directory.Exists(target)) throw new IOException("Вместо выходного файла указана папка.");
            if (File.Exists(target) && !overwrite)
                throw new IOException("Результат уже существует. Укажите другое имя или --overwrite.");

            var timer = Stopwatch.StartNew();
            using var reduced = Reduce(source, cellSize);
            CliApplication.SaveImage(reduced, target, overwrite);
            timer.Stop();

            if (values.ContainsKey("--json"))
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    process = "grid-reduction",
                    input,
                    output = target,
                    cellSize,
                    sourceSize = new { width = source.Width, height = source.Height },
                    outputSize = new { width = reduced.Width, height = reduced.Height },
                    elapsedSeconds = timer.Elapsed.TotalSeconds
                }, new JsonSerializerOptions { WriteIndented = true }));
            else
                output.WriteLine($"Сетка сжата: {target}; {source.Width} × {source.Height} → " +
                    $"{reduced.Width} × {reduced.Height}; ячейка {cellSize} px; " +
                    $"{timer.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)} с.");
            return 0;

            void Add(string name, string value)
            {
                if (string.IsNullOrWhiteSpace(value)) throw new CliUsageException($"Пустое значение {name}.");
                if (!values.TryAdd(name, value)) throw new CliUsageException($"Параметр {name} указан несколько раз.");
            }
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (ArgumentException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(ex is AggregateException ? ex.GetBaseException().Message : ex.Message); return 1; }
    }

    private static Bitmap Reduce(Bitmap source, int cellSize)
    {
        try { return PixelGridReducer.Reduce(source, cellSize); }
        catch (ArgumentException ex) { throw new IOException(ex.Message, ex); }
    }
}
