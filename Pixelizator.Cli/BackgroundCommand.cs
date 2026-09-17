using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PixelArtDownscale;

namespace Pixelizator.Cli;

internal static class BackgroundCommand
{
    internal const string Help = """
        Tessera remove-background — удаление однотонного фона в прозрачность.
        Использование: tessera remove-background input.png [-o output.png] [параметры]

          -i, --input PATH           PNG, JPG/JPEG, BMP, GIF или TIFF.
          -o, --output PATH          Только PNG; по умолчанию <имя>_transparent.png.
          --mode VALUE               global или edges (global).
          --background-color VALUE   auto, white, black или #RRGGBB (auto).
          --tolerance N              Допуск совпадения цвета: 0–100% (8).
          --overwrite                Разрешить замену выходного файла.
          --json                     Вывести параметры и время в JSON.
          -h, --help                 Эта справка.

        global удаляет похожий цвет во всём изображении. edges начинает с четырёх
        краёв и сохраняет совпадающие участки, замкнутые внутри объекта.
        Команда remove-bg является коротким псевдонимом.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"]) { output.WriteLine(Help); return 0; }

        try
        {
            BackgroundArguments command;
            try { command = Parse(args); }
            catch (ArgumentException ex) { throw new CliUsageException(ex.Message); }

            if (!File.Exists(command.Input))
                throw new IOException($"Исходный файл не найден: {command.Input}");
            if (Directory.Exists(command.Output))
                throw new IOException("Вместо выходного файла указана папка.");
            if (File.Exists(command.Output) && !command.Overwrite)
                throw new IOException("Результат уже существует. Укажите другое имя или --overwrite.");

            var timer = Stopwatch.StartNew();
            using var source = LoadOrientedImage(command.Input);
            using var result = BackgroundRemover.Remove(source, command.Mode, command.Tolerance, command.BackgroundColor);
            CliApplication.SaveImage(result, command.Output, command.Overwrite);
            timer.Stop();

            if (command.Json)
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    process = "background-removal",
                    input = command.Input,
                    output = command.Output,
                    width = result.Width,
                    height = result.Height,
                    mode = Name(command.Mode),
                    backgroundColor = ColorName(command.BackgroundColor),
                    tolerance = command.Tolerance,
                    elapsedSeconds = timer.Elapsed.TotalSeconds
                }, new JsonSerializerOptions { WriteIndented = true }));
            else
                output.WriteLine($"Фон удалён: {command.Output}; режим: {Name(command.Mode)}; " +
                    $"цвет: {ColorName(command.BackgroundColor)}; допуск: {command.Tolerance}%; " +
                    $"{timer.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)} с.");
            return 0;
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(ex is AggregateException ? ex.GetBaseException().Message : ex.Message); return 1; }
    }

    private static BackgroundArguments Parse(string[] args)
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
            else if (name is "--input" or "--output" or "--mode" or "--background-color" or "--tolerance")
            {
                if (equals >= 0) Add(name, token[(equals + 1)..]);
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                         && args[i + 1] is not "-i" and not "-o" and not "-h")
                    Add(name, args[++i]);
                else throw new CliUsageException($"Для {name} требуется значение.");
            }
            else throw new CliUsageException($"Неизвестный параметр удаления фона: {name}.");
        }

        if (!values.TryGetValue("--input", out string? input)) throw new CliUsageException("Укажите исходное изображение.");
        input = Path.GetFullPath(input);
        string target = Path.GetFullPath(values.GetValueOrDefault("--output",
            Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + "_transparent.png")));
        if (string.Equals(input, target, StringComparison.OrdinalIgnoreCase)) throw new CliUsageException("Исходник нельзя перезаписывать.");
        if (!Path.GetExtension(target).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException("Результат удаления фона нужно сохранить в PNG.");

        var mode = Choice("--mode", BackgroundRemovalMode.GlobalColor,
            ("global", BackgroundRemovalMode.GlobalColor), ("global-color", BackgroundRemovalMode.GlobalColor),
            ("edges", BackgroundRemovalMode.EdgeConnected), ("edge-connected", BackgroundRemovalMode.EdgeConnected));
        int tolerance = Integer("--tolerance", 8, 0, 100);
        Color? color = values.TryGetValue("--background-color", out string? colorName) ? ParseColor(colorName) : null;
        return new BackgroundArguments(input, target, mode, tolerance, color,
            values.ContainsKey("--overwrite"), values.ContainsKey("--json"));

        void Add(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new CliUsageException($"Значение {name} не может быть пустым.");
            if (!values.TryAdd(name, value)) throw new CliUsageException($"Параметр {name} указан несколько раз.");
        }

        int Integer(string name, int fallback, int min, int max)
        {
            if (!values.TryGetValue(name, out string? value)) return fallback;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result) || result < min || result > max)
                throw new CliUsageException($"{name}: требуется целое число от {min} до {max}.");
            return result;
        }

        T Choice<T>(string name, T fallback, params (string Name, T Value)[] choices)
        {
            if (!values.TryGetValue(name, out string? value)) return fallback;
            foreach (var choice in choices)
                if (string.Equals(value, choice.Name, StringComparison.OrdinalIgnoreCase)) return choice.Value;
            throw new CliUsageException($"{name}: допустимы {string.Join(", ", choices.Select(q => q.Name).Distinct())}.");
        }
    }

    private static Color? ParseColor(string value)
    {
        if (value.Equals("auto", StringComparison.OrdinalIgnoreCase)) return null;
        if (value.Equals("white", StringComparison.OrdinalIgnoreCase)) return Color.White;
        if (value.Equals("black", StringComparison.OrdinalIgnoreCase)) return Color.Black;
        string hex = value.StartsWith('#') ? value[1..] : value;
        if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        throw new CliUsageException("--background-color: допустимы auto, white, black или цвет #RRGGBB.");
    }

    private static Bitmap LoadOrientedImage(string path)
    {
        var source = CliApplication.LoadImage(path);
        try
        {
            const int orientationId = 0x0112;
            if (!source.PropertyIdList.Contains(orientationId)) return source;
            byte[]? value = source.GetPropertyItem(orientationId)?.Value;
            if (value is null || value.Length < 2) return source;
            source.RotateFlip(BitConverter.ToUInt16(value, 0) switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone
            });
            return source;
        }
        catch { source.Dispose(); throw; }
    }

    private static string Name(BackgroundRemovalMode value) => value == BackgroundRemovalMode.GlobalColor ? "global" : "edges";
    private static string ColorName(Color? value) => value is null ? "auto" : $"#{value.Value.R:X2}{value.Value.G:X2}{value.Value.B:X2}";

    private sealed record BackgroundArguments(string Input, string Output, BackgroundRemovalMode Mode, int Tolerance,
        Color? BackgroundColor, bool Overwrite, bool Json);
}
