using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text.Json;
using Tessera.Rendering;
using DrawingBitmap = System.Drawing.Bitmap;
using MediaColor = System.Windows.Media.Color;

namespace Tessera.Cli;

internal static class BoardCommand
{
    private const string Help = """
        Tessera board — создать, наполнить и экспортировать редактируемую доску.
        Использование:
          tessera board create -o BOARD.tboard --width N --height N [--background #RRGGBB]
          tessera board add BOARD.tboard --image IMAGE -o NEW.tboard
                  [--name NAME] [--x N] [--y N] [--angle N] [--scale N]
          tessera board export BOARD.tboard -o OUTPUT.png
          tessera board transform BOARD.tboard --index N -o NEW.tboard [--x N] [--y N] [--angle N] [--scale N]
          tessera board remove BOARD.tboard --index N -o NEW.tboard
          tessera board reorder BOARD.tboard --from N --to N -o NEW.tboard
          tessera board background BOARD.tboard --color #RRGGBB -o NEW.tboard
          tessera board info BOARD.tboard [--json]
        Все операции записи принимают --overwrite. Добавление создаёт новую доску;
        для изменения исходного файла укажите его же в -o и --overwrite.
        """;

    private sealed class Manifest
    {
        public int Version { get; set; } = 2;
        public int Width { get; set; }
        public int Height { get; set; }
        public string BackgroundColor { get; set; } = "#FFFFFF";
        public string[] SourcePaths { get; set; } = [];
        public List<Layer> Layers { get; set; } = [];
    }

    private sealed class Layer
    {
        public string Name { get; set; } = "";
        public string ImageEntry { get; set; } = "";
        public double Width { get; set; }
        public double Height { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Angle { get; set; }
        public double Scale { get; set; } = 1;
    }

    private sealed class LegacyBoard
    {
        public int Version { get; set; } = 1;
        public int Width { get; set; }
        public int Height { get; set; }
        public List<LegacyLayer> Layers { get; set; } = [];
    }

    private sealed class LegacyLayer
    {
        public string Name { get; set; } = "";
        public byte[] Png { get; set; } = [];
        public double Width { get; set; }
        public double Height { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Angle { get; set; }
        public double Scale { get; set; } = 1;
    }

    private sealed record Board(Manifest Manifest, List<byte[]> Images);

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            output.WriteLine(Help);
            return 0;
        }
        try
        {
            return args[0] switch
            {
                "create" => Create(args[1..], output),
                "add" => Add(args[1..], output),
                "export" => Export(args[1..], output),
                "info" => Info(args[1..], output),
                "transform" => Transform(args[1..], output),
                "remove" => Remove(args[1..], output),
                "reorder" => Reorder(args[1..], output),
                "background" => Background(args[1..], output),
                _ => throw new CliUsageException($"Неизвестная команда board: {args[0]}.")
            };
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(ex is AggregateException ? ex.GetBaseException().Message : ex.Message); return 1; }
    }

    private static int Create(string[] args, TextWriter output)
    {
        if (ShowHelp(args, output)) return 0;
        var options = Parse(args, ["--output", "--width", "--height", "--background"], ["--overwrite", "--json"]);
        if (options.Input is not null) throw new CliUsageException("board create не принимает исходный файл.");
        string target = RequiredPath(options, "--output", ".tboard");
        int width = Integer(options, "--width", 0, 128, 16384), height = Integer(options, "--height", 0, 128, 16384);
        if (width == 0 || height == 0) throw new CliUsageException("Укажите --width и --height.");
        string background = Color(options, "--background", "#FFFFFF");
        ValidateOutput(target, null, options);
        var board = new Board(new Manifest { Width = width, Height = height, BackgroundColor = background }, []);
        WriteBoard(target, board);
        WriteReport("board-create", target, board, options, output);
        return 0;
    }

    private static int Add(string[] args, TextWriter output)
    {
        if (ShowHelp(args, output)) return 0;
        var options = Parse(args, ["--output", "--image", "--name", "--x", "--y", "--angle", "--scale"],
            ["--overwrite", "--json"]);
        string source = RequiredInput(options);
        string target = RequiredPath(options, "--output", ".tboard");
        string imagePath = RequiredPath(options, "--image", null);
        ValidateOutput(target, source, options);
        Board board = ReadBoard(source);
        using var image = CliApplication.LoadImage(imagePath);
        byte[] png;
        using (var stream = new MemoryStream()) { image.Save(stream, ImageFormat.Png); png = stream.ToArray(); }
        double x = Number(options, "--x", 0, -1e9, 1e9), y = Number(options, "--y", 0, -1e9, 1e9);
        double angle = Number(options, "--angle", 0, -360000, 360000);
        double scale = Number(options, "--scale", 1, .1, 3);
        int index = board.Manifest.Layers.Count;
        if (index >= 500) throw new CliUsageException("Доска содержит максимум 500 слоёв.");
        board.Manifest.Layers.Add(new Layer
        {
            Name = options.Values.GetValueOrDefault("--name", Path.GetFileName(imagePath)),
            ImageEntry = $"images/{index:D4}.bin", Width = image.Width, Height = image.Height,
            X = x, Y = y, Angle = angle, Scale = scale
        });
        board.Images.Add(png);
        board.Manifest.SourcePaths = (board.Manifest.SourcePaths ?? []).Append(imagePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        WriteBoard(target, board);
        WriteReport("board-add", target, board, options, output);
        return 0;
    }

    private static int Export(string[] args, TextWriter output)
    {
        if (ShowHelp(args, output)) return 0;
        var options = Parse(args, ["--output"], ["--overwrite", "--json"]);
        string source = RequiredInput(options);
        string target = RequiredPath(options, "--output", ".png");
        ValidateOutput(target, source, options);
        Board board = ReadBoard(source);
        string hex = board.Manifest.BackgroundColor;
        MediaColor color = MediaColor.FromRgb(Convert.ToByte(hex.Substring(1, 2), 16),
            Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));
        MoodboardRenderLayer[] layers = board.Manifest.Layers.Select((layer, q) => new MoodboardRenderLayer(
            board.Images[q], layer.Width, layer.Height, layer.X, layer.Y, layer.Angle, layer.Scale)).ToArray();
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try { MoodboardPngExporter.Export(target, board.Manifest.Width, board.Manifest.Height, color, layers); }
            catch (Exception ex) { failure = ex; }
        });
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start(); worker.Join();
        if (failure is not null) throw failure;
        WriteReport("board-export", target, board, options, output);
        return 0;
    }

    private static int Info(string[] args, TextWriter output)
    {
        if (ShowHelp(args, output)) return 0;
        var options = Parse(args, [], ["--json"]);
        string source = RequiredInput(options);
        Board board = ReadBoard(source);
        if (options.Values.ContainsKey("--json")) output.WriteLine(JsonSerializer.Serialize(new
        {
            process = "board-info", input = source, board.Manifest.Width, board.Manifest.Height,
            board.Manifest.BackgroundColor, board.Manifest.SourcePaths, board.Manifest.Layers
        }, new JsonSerializerOptions { WriteIndented = true }));
        else
        {
            output.WriteLine($"Доска: {board.Manifest.Width} × {board.Manifest.Height}; слоёв: {board.Manifest.Layers.Count}");
            foreach (Layer layer in board.Manifest.Layers) output.WriteLine($"{layer.Name}: {layer.Width} × {layer.Height} @ ({layer.X}, {layer.Y}), угол {layer.Angle}, масштаб {layer.Scale}");
        }
        return 0;
    }

    private static int Transform(string[] args, TextWriter output)
    {
        if (ShowHelp(args, output)) return 0;
        var options = Parse(args, ["--output", "--index", "--x", "--y", "--angle", "--scale"], ["--overwrite", "--json"]);
        string source = RequiredInput(options), target = RequiredPath(options, "--output", ".tboard");
        ValidateOutput(target, source, options);
        Board board = ReadBoard(source);
        Layer layer = IndexedLayer(board, options, "--index");
        if (new[] { "--x", "--y", "--angle", "--scale" }.All(name => !options.Values.ContainsKey(name)))
            throw new CliUsageException("Укажите хотя бы один параметр положения, угла или масштаба.");
        layer.X = Number(options, "--x", layer.X, -1e9, 1e9);
        layer.Y = Number(options, "--y", layer.Y, -1e9, 1e9);
        layer.Angle = Number(options, "--angle", layer.Angle, -360000, 360000);
        layer.Scale = Number(options, "--scale", layer.Scale, .1, 3);
        WriteBoard(target, board); WriteReport("board-transform", target, board, options, output);
        return 0;
    }

    private static int Remove(string[] args, TextWriter output)
    {
        if (ShowHelp(args, output)) return 0;
        var options = Parse(args, ["--output", "--index"], ["--overwrite", "--json"]);
        string source = RequiredInput(options), target = RequiredPath(options, "--output", ".tboard");
        ValidateOutput(target, source, options);
        Board board = ReadBoard(source);
        int index = Index(board, options, "--index");
        board.Manifest.Layers.RemoveAt(index); board.Images.RemoveAt(index);
        Renumber(board);
        WriteBoard(target, board); WriteReport("board-remove", target, board, options, output);
        return 0;
    }

    private static int Reorder(string[] args, TextWriter output)
    {
        if (ShowHelp(args, output)) return 0;
        var options = Parse(args, ["--output", "--from", "--to"], ["--overwrite", "--json"]);
        string source = RequiredInput(options), target = RequiredPath(options, "--output", ".tboard");
        ValidateOutput(target, source, options);
        Board board = ReadBoard(source);
        int from = Index(board, options, "--from"), to = Index(board, options, "--to");
        Layer layer = board.Manifest.Layers[from]; byte[] data = board.Images[from];
        board.Manifest.Layers.RemoveAt(from); board.Images.RemoveAt(from);
        board.Manifest.Layers.Insert(to, layer); board.Images.Insert(to, data);
        Renumber(board);
        WriteBoard(target, board); WriteReport("board-reorder", target, board, options, output);
        return 0;
    }

    private static int Background(string[] args, TextWriter output)
    {
        if (ShowHelp(args, output)) return 0;
        var options = Parse(args, ["--output", "--color"], ["--overwrite", "--json"]);
        string source = RequiredInput(options), target = RequiredPath(options, "--output", ".tboard");
        ValidateOutput(target, source, options);
        if (!options.Values.ContainsKey("--color")) throw new CliUsageException("Укажите --color.");
        Board board = ReadBoard(source);
        board.Manifest.BackgroundColor = Color(options, "--color", "#FFFFFF");
        WriteBoard(target, board); WriteReport("board-background", target, board, options, output);
        return 0;
    }

    private static Layer IndexedLayer(Board board, Options options, string name) => board.Manifest.Layers[Index(board, options, name)];

    private static int Index(Board board, Options options, string name)
    {
        if (!options.Values.ContainsKey(name)) throw new CliUsageException($"Укажите {name}.");
        return Integer(options, name, 0, 1, board.Manifest.Layers.Count) - 1;
    }

    private static void Renumber(Board board)
    {
        for (int q = 0; q < board.Manifest.Layers.Count; q++)
            board.Manifest.Layers[q].ImageEntry = $"images/{q:D4}.bin";
    }

    private static Board ReadBoard(string path)
    {
        using var file = File.OpenRead(path);
        bool zipped = file.ReadByte() == 'P' && file.ReadByte() == 'K';
        file.Position = 0;
        if (!zipped)
        {
            var legacy = JsonSerializer.Deserialize<LegacyBoard>(file) ?? throw new InvalidDataException("Пустая доска.");
            if (legacy.Version != 1) throw new InvalidDataException("Неподдерживаемая версия доски.");
            if (legacy.Layers is null) throw new InvalidDataException("В доске нет списка слоёв.");
            var manifest = new Manifest { Width = legacy.Width, Height = legacy.Height };
            ValidateBoardHeader(manifest, legacy.Layers.Count);
            var images = new List<byte[]>(legacy.Layers.Count);
            for (int q = 0; q < legacy.Layers.Count; q++)
            {
                LegacyLayer layer = legacy.Layers[q];
                manifest.Layers.Add(new Layer { Name = layer.Name, ImageEntry = $"images/{q:D4}.bin",
                    Width = layer.Width, Height = layer.Height, X = layer.X, Y = layer.Y,
                    Angle = layer.Angle, Scale = layer.Scale });
                images.Add(layer.Png);
            }
            ValidateBoard(manifest, images);
            return new(manifest, images);
        }
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        Manifest board;
        using (var manifestStream = archive.GetEntry("board.json")?.Open() ?? throw new InvalidDataException("В доске нет board.json."))
            board = JsonSerializer.Deserialize<Manifest>(manifestStream) ?? throw new InvalidDataException("Пустая доска.");
        if (board.Version != 2) throw new InvalidDataException("Неподдерживаемая версия доски.");
        if (board.Layers is null) throw new InvalidDataException("В доске нет списка слоёв.");
        ValidateBoardHeader(board, board.Layers.Count);
        var data = new List<byte[]>(board.Layers.Count);
        foreach (Layer layer in board.Layers)
        {
            if (!layer.ImageEntry.StartsWith("images/", StringComparison.Ordinal) || layer.ImageEntry.Contains("..", StringComparison.Ordinal))
                throw new InvalidDataException("Неверная запись слоя.");
            var entry = archive.GetEntry(layer.ImageEntry) ?? throw new InvalidDataException($"Нет изображения слоя: {layer.ImageEntry}.");
            if (entry.Length is <= 0 or > 536870912) throw new InvalidDataException("Недопустимый размер изображения слоя.");
            byte[] bytes = new byte[(int)entry.Length];
            using (var stream = entry.Open()) stream.ReadExactly(bytes);
            data.Add(bytes);
        }
        ValidateBoard(board, data);
        return new(board, data);
    }

    private static void WriteBoard(string path, Board board)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".tessera-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                for (int q = 0; q < board.Images.Count; q++)
                {
                    using var stream = archive.CreateEntry(board.Manifest.Layers[q].ImageEntry, CompressionLevel.NoCompression).Open();
                    stream.Write(board.Images[q]);
                }
                using var manifest = archive.CreateEntry("board.json", CompressionLevel.Fastest).Open();
                JsonSerializer.Serialize(manifest, board.Manifest);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void ValidateBoard(Manifest board, IReadOnlyList<byte[]> images)
    {
        ValidateBoardHeader(board, images.Count);
        if (board.Layers.Count != images.Count)
            throw new InvalidDataException("Неверный размер доски или число слоёв.");
        _ = Color(new Options(null, new() { ["--background"] = board.BackgroundColor }), "--background", "#FFFFFF");
        for (int q = 0; q < board.Layers.Count; q++)
        {
            Layer layer = board.Layers[q];
            if (!double.IsFinite(layer.Width) || !double.IsFinite(layer.Height) || layer.Width <= 0 || layer.Height <= 0 ||
                !double.IsFinite(layer.X) || !double.IsFinite(layer.Y) || !double.IsFinite(layer.Angle) ||
                !double.IsFinite(layer.Scale) || layer.Scale is < .1 or > 3 || images[q].Length == 0)
                throw new InvalidDataException($"Неверный слой доски: {q}.");
        }
    }

    private static void ValidateBoardHeader(Manifest board, int layerCount)
    {
        if (board.Width is < 128 or > 16384 || board.Height is < 128 or > 16384 || layerCount is < 0 or > 500)
            throw new InvalidDataException("Неверный размер доски или число слоёв.");
    }

    private static void WriteReport(string process, string target, Board board, Options options, TextWriter output)
    {
        if (options.Values.ContainsKey("--json")) output.WriteLine(JsonSerializer.Serialize(new
        { process, output = target, board.Manifest.Width, board.Manifest.Height, layerCount = board.Manifest.Layers.Count },
            new JsonSerializerOptions { WriteIndented = true }));
        else output.WriteLine($"Сохранено: {target}; слоёв: {board.Manifest.Layers.Count}");
    }

    private sealed record Options(string? Input, Dictionary<string, string> Values);

    private static Options Parse(string[] args, IReadOnlyCollection<string> valueOptions, IReadOnlyCollection<string> flagOptions)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string? input = null;
        for (int q = 0; q < args.Length; q++)
        {
            string token = args[q];
            if (!token.StartsWith('-'))
            {
                if (input is not null) throw new CliUsageException("Укажите только одну доску.");
                input = Path.GetFullPath(token); continue;
            }
            int equals = token.IndexOf('=');
            string name = equals < 0 ? token : token[..equals];
            if (name == "-o") name = "--output";
            if (flagOptions.Contains(name))
            {
                if (equals >= 0) throw new CliUsageException($"{name} не принимает значение.");
                if (!values.TryAdd(name, "true")) throw new CliUsageException($"{name} указан дважды.");
                continue;
            }
            if (!valueOptions.Contains(name)) throw new CliUsageException($"Неизвестный параметр: {name}.");
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

    private static string RequiredInput(Options options) => options.Input ?? throw new CliUsageException("Укажите файл доски.");

    private static string RequiredPath(Options options, string name, string? extension)
    {
        if (!options.Values.TryGetValue(name, out string? value)) throw new CliUsageException($"Укажите {name}.");
        string path = Path.GetFullPath(value);
        if (extension is not null && !Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException($"{name} должен иметь расширение {extension}.");
        return path;
    }

    private static int Integer(Options options, string name, int fallback, int min, int max)
    {
        if (!options.Values.TryGetValue(name, out string? value)) return fallback;
        if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture,
            out int number) || number < min || number > max)
            throw new CliUsageException($"{name}: требуется целое число от {min} до {max}.");
        return number;
    }

    private static double Number(Options options, string name, double fallback, double min, double max)
    {
        if (!options.Values.TryGetValue(name, out string? value)) return fallback;
        if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
            out double number) || !double.IsFinite(number) || number < min || number > max)
            throw new CliUsageException($"{name}: требуется число от {min} до {max}.");
        return number;
    }

    private static string Color(Options options, string name, string fallback)
    {
        string value = options.Values.GetValueOrDefault(name, fallback);
        if (value.Length != 7 || value[0] != '#' || !int.TryParse(value.AsSpan(1),
            System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out _))
            throw new CliUsageException($"{name}: требуется цвет #RRGGBB.");
        return value.ToUpperInvariant();
    }

    private static void ValidateOutput(string output, string? source, Options options)
    {
        if (source is not null && string.Equals(source, output, StringComparison.OrdinalIgnoreCase) &&
            !options.Values.ContainsKey("--overwrite"))
            throw new CliUsageException("Для изменения исходной доски укажите --overwrite.");
        if (Directory.Exists(output)) throw new IOException($"Вместо файла указана папка: {output}.");
        if (File.Exists(output) && !options.Values.ContainsKey("--overwrite"))
            throw new IOException($"Файл уже существует: {output}. Добавьте --overwrite.");
    }
}
