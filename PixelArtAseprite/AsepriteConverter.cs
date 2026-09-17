using System.Text.Json;

namespace PixelArtAseprite;

public static class AsepriteConverter
{
    public static JsonSerializerOptions JsonOptions => new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static AsepriteExportResult Convert(string inputPath, string outputDirectory, AsepriteExportOptions? options = null,
        AsepriteLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        options ??= new AsepriteExportOptions();
        string input = Path.GetFullPath(inputPath), directory = Path.GetFullPath(outputDirectory);
        if (Path.GetExtension(input).ToLowerInvariant() is not (".ase" or ".aseprite"))
            throw new ArgumentException("Требуется файл .ase или .aseprite.", nameof(inputPath));
        string name = Path.GetFileNameWithoutExtension(input);
        string imagePath = Path.Combine(directory, name + ".png"), jsonPath = Path.Combine(directory, name + ".json");
        string? inspectionPath = options.IncludeInspection ? Path.Combine(directory, name + ".inspection.json") : null;
        string[] targets = inspectionPath is null ? [imagePath, jsonPath] : [imagePath, jsonPath, inspectionPath];
        foreach (string target in targets) CheckTarget(target);
        var document = AsepriteReader.Read(input, limits, cancellationToken);
        var sheet = SpriteSheet.Create(document, options, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        var staged = new List<string>();
        var published = new List<string>();
        try
        {
            using (var stream = Stage()) PngWriter.Write(stream, sheet.Image, document.IsSrgb, cancellationToken);
            using (var stream = Stage()) JsonSerializer.Serialize(stream, sheet.Metadata, JsonOptions);
            if (inspectionPath is not null)
            {
                using var stream = Stage();
                JsonSerializer.Serialize(stream, new
                {
                    schema = "aseprite-inspection/v1", document.Source, document.SourceSha256,
                    document.Width, document.Height, depth = 32, layer = document.LayerName,
                    profile = document.IsSrgb ? "srgb" : "unspecified", frameCount = document.Frames.Count,
                    document.TotalDurationMs, document.Tags, document.Warnings,
                    frames = document.Frames.Select(f => new { f.Index, f.DurationMs,
                        cel = f.Cel is { } cel ? new { cel.Type, cel.X, cel.Y, cel.LinkedFrame, cel.Image!.Width, cel.Image.Height } : null })
                }, JsonOptions);
            }
            for (int q = 0; q < targets.Length; q++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(staged[q], targets[q], overwrite: false);
                published.Add(targets[q]);
            }
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            foreach (string path in published.Concat(staged))
            {
                try { File.Delete(path); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { errors.Add(cleanup); }
            }
            if (errors.Count > 1) throw new AggregateException("Экспорт не завершён; не удалось удалить часть созданных файлов.", errors);
            throw;
        }
        return new AsepriteExportResult(input, imagePath, jsonPath, inspectionPath, document.Frames.Count,
            sheet.Image.Width, sheet.Image.Height, document.TotalDurationMs, document.Warnings);

        FileStream Stage()
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.Combine(directory, $".aseprite-{Guid.NewGuid():N}.tmp");
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            staged.Add(path);
            return stream;
        }
    }

    /// <summary>Последовательно экспортирует файлы. Ошибка одного документа не останавливает остальные; отмена останавливает партию.</summary>
    public static IReadOnlyList<AsepriteBatchItem> ConvertBatch(IEnumerable<string> inputPaths, string outputDirectory,
        AsepriteExportOptions? options = null, AsepriteLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        var result = new List<AsepriteBatchItem>();
        foreach (string input in inputPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { result.Add(new AsepriteBatchItem(input, Convert(input, outputDirectory, options, limits, cancellationToken), null)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or AggregateException)
            {
                result.Add(new AsepriteBatchItem(input, null, ex.Message));
            }
        }
        return result.AsReadOnly();
    }

    private static void CheckTarget(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException($"Выход уже существует: {path}");
    }
}
