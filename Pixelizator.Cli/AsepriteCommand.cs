using System.Globalization;
using System.Text.Json;
using PixelArtAseprite;

namespace Pixelizator.Cli;

internal static class AsepriteCommand
{
    internal const string Help = """
        Tessera aseprite — преобразовать .ase/.aseprite в PNG-спрайтлист и JSON.
        Использование: tessera aseprite file.aseprite [ещё.ase] --output-dir export

          -i, --input PATH       Добавить входной файл; можно повторять.
          -d, --output-dir PATH  Папка результатов (рядом с первым файлом: aseprite-export).
          --layout VALUE        horizontal, vertical, grid (horizontal).
          --columns N           Столбцы сетки: 1–256 (4), только для grid.
          --padding N           Промежуток между кадрами: 0–128 (0).
          --inspection          Дополнительно сохранить технический .inspection.json.
          --json                Отчёт по каждому файлу в JSON.
          -h, --help            Эта справка.

        Каждый вход даёт <имя>.png и <имя>.json. Результаты не перезаписываются.
        Ошибка одного файла не останавливает остальные; итоговый код тогда 1.
        Поддерживается ограниченный набор RGBA32 Aseprite с одним видимым слоем.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"]) { output.WriteLine(Help); return 0; }

        try
        {
            var command = Parse(args);
            var results = AsepriteConverter.ConvertBatch(command.Inputs, command.Directory, command.Options);
            bool failed = results.Any(item => !item.Success);
            if (command.Json)
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    process = "aseprite-export",
                    outputDirectory = command.Directory,
                    options = new
                    {
                        layout = command.Options.Layout.ToString().ToLowerInvariant(),
                        command.Options.Columns,
                        command.Options.Padding,
                        command.Options.IncludeInspection
                    },
                    succeeded = results.Count(item => item.Success),
                    failed = results.Count(item => !item.Success),
                    items = results.Select(item => new
                    {
                        input = item.Source,
                        success = item.Success,
                        image = item.Result?.ImagePath,
                        metadata = item.Result?.JsonPath,
                        inspection = item.Result?.InspectionPath,
                        frames = item.Result?.FrameCount,
                        width = item.Result?.Width,
                        height = item.Result?.Height,
                        durationMs = item.Result?.TotalDurationMs,
                        warnings = item.Result?.Warnings,
                        error = item.Error
                    })
                }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else
            {
                foreach (var item in results)
                {
                    if (item.Result is { } result)
                        output.WriteLine($"Сохранено: {result.ImagePath}; {result.JsonPath}; {result.FrameCount} кадров.");
                    else
                        error.WriteLine($"Ошибка {item.Source}: {item.Error}");
                }
                output.WriteLine($"Готово: {results.Count(item => item.Success)}. Ошибок: {results.Count(item => !item.Success)}.");
            }
            return failed ? 1 : 0;
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(ex is AggregateException ? ex.GetBaseException().Message : ex.Message); return 1; }
    }

    private static Command Parse(string[] args)
    {
        var inputs = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool positional = false;
        for (int i = 0; i < args.Length; i++)
        {
            string token = args[i];
            if (!positional && token == "--") { positional = true; continue; }
            if (positional || !token.StartsWith('-')) { AddInput(token); continue; }

            int equals = token.IndexOf('=');
            string name = equals < 0 ? token : token[..equals];
            name = name switch { "-i" => "--input", "-d" => "--output-dir", _ => name };
            if (name is "--inspection" or "--json")
            {
                if (equals >= 0 || !values.TryAdd(name, "true"))
                    throw new CliUsageException($"Повторный параметр или значение для {name}.");
            }
            else if (name is "--input" or "--output-dir" or "--layout" or "--columns" or "--padding")
            {
                string value;
                if (equals >= 0) value = token[(equals + 1)..];
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                         && args[i + 1] is not "-i" and not "-d" and not "-h")
                    value = args[++i];
                else throw new CliUsageException($"Для {name} требуется значение.");
                if (string.IsNullOrWhiteSpace(value)) throw new CliUsageException($"Пустое значение {name}.");
                if (name == "--input") AddInput(value);
                else if (!values.TryAdd(name, value)) throw new CliUsageException($"Параметр {name} указан несколько раз.");
            }
            else throw new CliUsageException($"Неизвестный параметр Aseprite: {name}.");
        }

        if (inputs.Count == 0) throw new CliUsageException("Укажите хотя бы один файл .ase или .aseprite.");
        SpriteSheetLayout layout = values.GetValueOrDefault("--layout", "horizontal").ToLowerInvariant() switch
        {
            "horizontal" => SpriteSheetLayout.Horizontal,
            "vertical" => SpriteSheetLayout.Vertical,
            "grid" => SpriteSheetLayout.Grid,
            _ => throw new CliUsageException("--layout: допустимы horizontal, vertical, grid.")
        };
        if (layout != SpriteSheetLayout.Grid && values.ContainsKey("--columns"))
            throw new CliUsageException("--columns используется только с --layout grid.");
        int columns = Number("--columns", 4, 1, 256);
        int padding = Number("--padding", 0, 0, 128);
        string directory = Path.GetFullPath(values.GetValueOrDefault("--output-dir",
            Path.Combine(Path.GetDirectoryName(inputs[0])!, "aseprite-export")));
        return new Command(inputs, directory, new AsepriteExportOptions
        {
            Layout = layout,
            Columns = columns,
            Padding = padding,
            IncludeInspection = values.ContainsKey("--inspection")
        }, values.ContainsKey("--json"));

        void AddInput(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new CliUsageException("Пустой путь исходного файла.");
            string full = Path.GetFullPath(path);
            if (inputs.Contains(full, StringComparer.OrdinalIgnoreCase))
                throw new CliUsageException($"Файл указан несколько раз: {full}");
            inputs.Add(full);
        }

        int Number(string name, int fallback, int min, int max)
        {
            if (!values.TryGetValue(name, out string? value)) return fallback;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < min || number > max)
                throw new CliUsageException($"{name}: требуется целое число от {min} до {max}.");
            return number;
        }
    }

    private sealed record Command(IReadOnlyList<string> Inputs, string Directory, AsepriteExportOptions Options, bool Json);
}
