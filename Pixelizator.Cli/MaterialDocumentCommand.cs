using System.Drawing;
using System.Numerics;
using System.Text.Json;
using PixelArtDownscale;

namespace Pixelizator.Cli;

internal static class MaterialDocumentCommand
{
    private const string Help = """
        Tessera material-document — работать с редактируемыми картами .tmaps.
        Использование:
          tessera material-document paint FILE.tmaps -o NEW.tmaps --map height|normal|ao
              --x N --y N [--to-x N] [--to-y N] [--size 1] [--value 0.5]
              [--normal-x 0] [--normal-y 0] [--normal-z 1] [--strength 1] [--softness 0] [--erase]
          tessera material-document settings FILE.tmaps -o NEW.tmaps
              [--normal-strength N] [--ao-strength N] [--ao-radius N] [--convention directx|opengl]
          tessera material-document reset FILE.tmaps -o NEW.tmaps --map normal|ao
          tessera material-document export FILE.tmaps [--output-dir DIR] [--overwrite] [--json]
          tessera material-document info FILE.tmaps [--json]
        Операции записи .tmaps требуют -o. Для изменения исходного файла добавьте --overwrite.
        """;

    private sealed record Options(string? Input, Dictionary<string, string> Values)
    {
        public string? Value(string name) => Values.GetValueOrDefault(name);
        public bool Flag(string name) => Values.ContainsKey(name);
    }

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            output.WriteLine(Help); return 0;
        }
        try
        {
            return args[0] switch
            {
                "paint" => Paint(args[1..], output),
                "settings" => Settings(args[1..], output),
                "reset" => Reset(args[1..], output),
                "export" => Export(args[1..], output),
                "info" => Info(args[1..], output),
                _ => throw new CliUsageException($"Неизвестная команда material-document: {args[0]}.")
            };
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(ex is AggregateException ? ex.GetBaseException().Message : ex.Message); return 1; }
    }

    private static int Paint(string[] args, TextWriter output)
    {
        if (ShowHelp(args, output)) return 0;
        Options options = Parse(args, ["--output", "--map", "--x", "--y", "--to-x", "--to-y", "--size",
            "--value", "--normal-x", "--normal-y", "--normal-z", "--strength", "--softness"],
            ["--erase", "--overwrite", "--json"]);
        string source = Input(options), target = Output(options, ".tmaps");
        ValidateOutput(source, target, options);
        var document = MaterialMapFile.Load(source);
        MaterialMapKind kind = Kind(options, allowHeight: true);
        if (options.Value("--x") is null || options.Value("--y") is null)
            throw new CliUsageException("Укажите --x и --y.");
        int x = Integer(options, "--x", 0, 0, document.Width - 1);
        int y = Integer(options, "--y", 0, 0, document.Height - 1);
        int toX = Integer(options, "--to-x", x, 0, document.Width - 1);
        int toY = Integer(options, "--to-y", y, 0, document.Height - 1);
        int size = Integer(options, "--size", 1, 1, 256);
        float value = (float)Number(options, "--value", .5, 0, 1);
        float strength = (float)Number(options, "--strength", 1, 0, 1);
        float softness = (float)Number(options, "--softness", 0, 0, 1);
        Vector3 normal = new((float)Number(options, "--normal-x", 0, -1, 1),
            (float)Number(options, "--normal-y", 0, -1, 1), (float)Number(options, "--normal-z", 1, -1, 1));
        if (normal.LengthSquared() < .000001f) throw new CliUsageException("Нормаль не может быть нулевой.");
        document.PaintLine(kind, x, y, toX, toY, size, value, normal, strength, softness, options.Flag("--erase"));
        if (kind == MaterialMapKind.Height) document.Recalculate();
        Save(document, target);
        Report("material-paint", source, target, document, options, output);
        return 0;
    }

    private static int Settings(string[] args, TextWriter output)
    {
        if (ShowHelp(args, output)) return 0;
        Options options = Parse(args, ["--output", "--normal-strength", "--ao-strength", "--ao-radius", "--convention"],
            ["--overwrite", "--json"]);
        string source = Input(options), target = Output(options, ".tmaps");
        ValidateOutput(source, target, options);
        if (new[] { "--normal-strength", "--ao-strength", "--ao-radius", "--convention" }.All(name => options.Value(name) is null))
            throw new CliUsageException("Укажите хотя бы одну настройку.");
        var document = MaterialMapFile.Load(source);
        document.NormalStrength = (float)Number(options, "--normal-strength", document.NormalStrength, 0, 32);
        document.AoStrength = (float)Number(options, "--ao-strength", document.AoStrength, 0, 16);
        document.AoRadius = Integer(options, "--ao-radius", document.AoRadius, 1, 64);
        document.DirectX = Choice(options, "--convention", document.DirectX ? "directx" : "opengl", "directx", "opengl") == "directx";
        document.Recalculate();
        Save(document, target);
        Report("material-settings", source, target, document, options, output);
        return 0;
    }

    private static int Reset(string[] args, TextWriter output)
    {
        if (ShowHelp(args, output)) return 0;
        Options options = Parse(args, ["--output", "--map"], ["--overwrite", "--json"]);
        string source = Input(options), target = Output(options, ".tmaps");
        ValidateOutput(source, target, options);
        var document = MaterialMapFile.Load(source);
        document.ResetManual(Kind(options, allowHeight: false));
        Save(document, target);
        Report("material-reset", source, target, document, options, output);
        return 0;
    }

    private static int Export(string[] args, TextWriter output)
    {
        if (ShowHelp(args, output)) return 0;
        Options options = Parse(args, ["--output-dir"], ["--overwrite", "--json"]);
        string source = Input(options);
        string directory = Path.GetFullPath(options.Value("--output-dir") ?? Path.GetDirectoryName(source)!);
        string name = Path.GetFileNameWithoutExtension(source);
        var kinds = new[] { MaterialMapKind.Height, MaterialMapKind.Normal, MaterialMapKind.AO };
        string[] targets = kinds.Select(kind => Path.Combine(directory, $"{name}_{kind.ToString().ToLowerInvariant()}.png")).ToArray();
        foreach (string target in targets) ValidateOutput(source, target, options);
        var document = MaterialMapFile.Load(source);
        for (int q = 0; q < kinds.Length; q++)
        {
            using Bitmap image = ImageToolsCommand.FromPixels(document.MapPixels(kinds[q]), document.Width, document.Height);
            CliApplication.SaveImage(image, targets[q], options.Flag("--overwrite"));
        }
        if (options.Flag("--json")) output.WriteLine(JsonSerializer.Serialize(new
        { process = "material-export", input = source, outputs = targets, document.Width, document.Height },
            new JsonSerializerOptions { WriteIndented = true }));
        else foreach (string target in targets) output.WriteLine($"Сохранено: {target}");
        return 0;
    }

    private static int Info(string[] args, TextWriter output)
    {
        if (ShowHelp(args, output)) return 0;
        Options options = Parse(args, [], ["--json"]);
        string source = Input(options);
        var document = MaterialMapFile.Load(source);
        if (options.Flag("--json")) output.WriteLine(JsonSerializer.Serialize(new
        { process = "material-info", input = source, document.Width, document.Height,
            document.NormalStrength, document.AoStrength, document.AoRadius,
            convention = document.DirectX ? "directx" : "opengl" }, new JsonSerializerOptions { WriteIndented = true }));
        else output.WriteLine($"Карты: {document.Width} × {document.Height}; Normal {document.NormalStrength}, AO {document.AoStrength} / {document.AoRadius}, {(document.DirectX ? "DirectX" : "OpenGL")}");
        return 0;
    }

    private static MaterialMapKind Kind(Options options, bool allowHeight)
    {
        string name = Choice(options, "--map", "", "height", "normal", "ao");
        if (name.Length == 0) throw new CliUsageException("Укажите --map.");
        if (!allowHeight && name == "height") throw new CliUsageException("Высота не имеет отдельного ручного слоя для сброса.");
        return name switch { "normal" => MaterialMapKind.Normal, "ao" => MaterialMapKind.AO, _ => MaterialMapKind.Height };
    }

    private static void Save(MaterialMapDocument document, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        MaterialMapFile.Save(document, path);
    }

    private static void Report(string process, string source, string target, MaterialMapDocument document,
        Options options, TextWriter output)
    {
        if (options.Flag("--json")) output.WriteLine(JsonSerializer.Serialize(new
        { process, input = source, output = target, document.Width, document.Height,
            document.NormalStrength, document.AoStrength, document.AoRadius,
            convention = document.DirectX ? "directx" : "opengl" }, new JsonSerializerOptions { WriteIndented = true }));
        else output.WriteLine($"Документ карт сохранён: {target}");
    }

    private static Options Parse(string[] args, IReadOnlyCollection<string> valuesAllowed, IReadOnlyCollection<string> flagsAllowed)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string? input = null;
        for (int q = 0; q < args.Length; q++)
        {
            string token = args[q];
            if (!token.StartsWith('-'))
            {
                if (input is not null) throw new CliUsageException("Укажите один документ .tmaps.");
                input = Path.GetFullPath(token); continue;
            }
            int equals = token.IndexOf('=');
            string name = equals < 0 ? token : token[..equals];
            if (name == "-o") name = "--output";
            if (flagsAllowed.Contains(name))
            {
                if (equals >= 0 || !values.TryAdd(name, "true")) throw new CliUsageException($"Неверный или повторный флаг {name}.");
                continue;
            }
            if (!valuesAllowed.Contains(name)) throw new CliUsageException($"Неизвестный параметр {name}.");
            string value = equals >= 0 ? token[(equals + 1)..] : q + 1 < args.Length ? args[++q] : "";
            if (string.IsNullOrWhiteSpace(value) || !values.TryAdd(name, value))
                throw new CliUsageException($"Нет значения или повторён параметр {name}.");
        }
        return new(input, values);
    }

    private static bool ShowHelp(string[] args, TextWriter output)
    {
        if (args.Length != 0 && args is not ["--help"] and not ["-h"]) return false;
        output.WriteLine(Help); return true;
    }

    private static string Input(Options options)
    {
        if (options.Input is null) throw new CliUsageException("Укажите файл .tmaps.");
        if (!Path.GetExtension(options.Input).Equals(".tmaps", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException("Ожидается файл .tmaps.");
        return options.Input;
    }

    private static string Output(Options options, string extension)
    {
        string path = options.Value("--output") is { } value ? Path.GetFullPath(value)
            : throw new CliUsageException("Укажите -o / --output.");
        if (!Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException($"Результат должен иметь расширение {extension}.");
        return path;
    }

    private static void ValidateOutput(string source, string target, Options options)
    {
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase) && !options.Flag("--overwrite"))
            throw new CliUsageException("Для изменения исходного документа укажите --overwrite.");
        if (Directory.Exists(target)) throw new IOException($"Вместо файла указана папка: {target}.");
        if (File.Exists(target) && !options.Flag("--overwrite"))
            throw new IOException($"Файл уже существует: {target}. Добавьте --overwrite.");
    }

    private static int Integer(Options options, string name, int fallback, int min, int max)
    {
        if (options.Value(name) is not { } value) return fallback;
        if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture,
            out int result) || result < min || result > max)
            throw new CliUsageException($"{name}: требуется целое число от {min} до {max}.");
        return result;
    }

    private static double Number(Options options, string name, double fallback, double min, double max)
    {
        if (options.Value(name) is not { } value) return fallback;
        if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
            out double result) || !double.IsFinite(result) || result < min || result > max)
            throw new CliUsageException($"{name}: требуется число от {min} до {max}.");
        return result;
    }

    private static string Choice(Options options, string name, string fallback, params string[] choices)
    {
        if (options.Value(name) is not { } value) return fallback;
        return choices.FirstOrDefault(choice => choice.Equals(value, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliUsageException($"{name}: допустимы {string.Join(", ", choices)}.");
    }
}
