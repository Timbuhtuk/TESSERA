using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using PixelArtDownscale;
using PixelArtAlignment;
using PixelArtWorkflows;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public sealed class EditorSettings
{
    public int Mode { get; set; } = 1;
    public DownscaleOptions Downscale { get; set; } = new();
    public int CellSize { get; set; } = 8;
    public bool DetectGrid { get; set; }
    public GridAlignmentMode AlignmentMode { get; set; } = GridAlignmentMode.Global;
    public bool AspectLock { get; set; }
}

public sealed class SourceEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OriginalPath { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public int Width { get; set; }
    public int Height { get; set; }
    public string? DisplayName { get; set; }
    public int AlignedCellSize { get; set; }
    public Guid? OriginSourceId { get; set; }
    public Guid? OriginGenerationId { get; set; }
    [JsonIgnore] public string Label => DisplayName ?? Path.GetFileName(OriginalPath);
    [JsonIgnore] public bool IsHeader => false;
    [JsonIgnore] public string Description => $"{Label}\n{Width} × {Height}\n{OriginalPath}";
    [JsonIgnore] public string RemoveDescription => Localization.Format("Удалить исходник «{0}» и его результаты из программы", Label);
    [JsonIgnore] public BitmapSource? Thumbnail { get; set; }
    [JsonIgnore] public ObservableCollection<GenerationEntry> Generations { get; } = [];
    [JsonIgnore] public GenerationEntry? SelectedGeneration { get; set; }
    [JsonIgnore] public EditorSettings? DraftSettings { get; set; }
}

public sealed class GenerationEntry : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string Caption { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Log { get; set; } = "";
    public bool IsAlignment { get; set; }
    public bool IsGridReduction { get; set; }
    public Guid? ParentGenerationId { get; set; }
    public bool PreservesTransparency { get; set; }
    public bool HasCrop { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int CellSize { get; set; }
    public EditorSettings Settings { get; set; } = new();
    public string? OperationId { get; set; }
    public ProcessingRecipe? AppliedRecipe { get; set; }
    public ProcessingRecipe? RecipeSnapshot { get; set; }
    public string? RecipeUnavailableReason { get; set; }
    private string _presentationHint = "";
    [JsonIgnore] public string PresentationHint
    {
        get => _presentationHint;
        set { if (_presentationHint == value) return; _presentationHint = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    [JsonIgnore] public bool IsHeader => false;
    [JsonIgnore] public bool IsGlobalAlignment => IsAlignment && Settings.AlignmentMode == GridAlignmentMode.Global;
    [JsonIgnore] public string Label => IsAlignment ? Localization.Format(Settings.AlignmentMode == GridAlignmentMode.Local
        ? "Локальная сетка {0} px · {1:HH:mm:ss}" : "Сетка {0} px · {1:HH:mm:ss}", CellSize, CreatedAt)
        : $"{(Operation.Length > 0 ? (OperationId == ProcessingOperations.Macro ? Operation : Localization.StoredText(Operation)) + " · " : IsGridReduction ? "1 px · " : "")}{Width} × {Height} · {CreatedAt:HH:mm:ss}";
    [JsonIgnore] public string Description => $"{(OperationId == ProcessingOperations.Macro ? Caption : Localization.StoredText(Caption))}\n{CreatedAt:dd.MM.yyyy HH:mm:ss}\n{PresentationHint}\n{Localization.StoredText(Log)}";
    [JsonIgnore] public string RemoveDescription => Localization.Format("Удалить результат «{0}» из истории", Label);
    [JsonIgnore] public BitmapSource? Thumbnail { get; set; }
}

/// <summary>Immutable PNG snapshots; a JSON record is committed only after its images are saved.</summary>
public sealed partial class ImageLibrary(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        options.Converters.Add(new RecipeRecordConverter());
        return options;
    }
    private sealed class RecipeRecordConverter : JsonConverter<ProcessingRecipe>
    {
        public override ProcessingRecipe? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => JsonSerializer.Deserialize<ProcessingRecipe>(ref reader, WorkflowJson.Options);
        public override void Write(Utf8JsonWriter writer, ProcessingRecipe recipe, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, recipe, WorkflowJson.Options);
    }
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public List<string> LoadWarnings { get; } = [];
    public string SourcePath(SourceEntry source) => Path.Combine(SourceDirectory(source), "source.png");
    public string ResultPath(SourceEntry source, GenerationEntry generation) => Path.Combine(GenerationDirectory(source, generation), "result.png");
    public string CropPath(SourceEntry source, GenerationEntry generation) => Path.Combine(GenerationDirectory(source, generation), "crop.png");
    private string SourceDirectory(SourceEntry source) => Path.Combine(DirectoryPath, source.Id.ToString("N"));
    private string GenerationDirectory(SourceEntry source, GenerationEntry generation) => Path.Combine(SourceDirectory(source), "generations", generation.Id.ToString("N"));

    public IReadOnlyList<SourceEntry> Load()
    {
        LoadWarnings.Clear();
        var sources = new List<SourceEntry>();
        if (!Directory.Exists(DirectoryPath)) return sources;
        foreach (string folder in Directory.EnumerateDirectories(DirectoryPath))
        {
            if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out Guid id)) continue;
            string manifest = Path.Combine(folder, "source.json");
            if (!File.Exists(manifest)) continue;
            try
            {
                var source = Read<SourceEntry>(manifest);
                if (source.Id != id) throw new InvalidDataException(Localization.Text("Идентификатор исходника не совпадает."));
                source.Thumbnail = ReadThumbnail(SourcePath(source));
                string generations = Path.Combine(folder, "generations");
                if (Directory.Exists(generations))
                {
                    var results = new List<GenerationEntry>();
                    foreach (string resultFolder in Directory.EnumerateDirectories(generations))
                    {
                        if (!Guid.TryParseExact(Path.GetFileName(resultFolder), "N", out Guid generationId)) continue;
                        string record = Path.Combine(resultFolder, "generation.json");
                        if (!File.Exists(record)) continue;
                        try
                        {
                            var generation = Read<GenerationEntry>(record);
                            if (generation.Id != generationId || generation.Settings?.Downscale is null) throw new InvalidDataException(Localization.Text("Повреждены параметры результата."));
                            HistoryRecipes.ValidateLegacyRecord(generation, record);
                            generation.Thumbnail = ReadThumbnail(ResultPath(source, generation));
                            if (generation.HasCrop && !File.Exists(CropPath(source, generation))) throw new InvalidDataException(Localization.Text("Отсутствует область обработки."));
                            results.Add(generation);
                        }
                        catch (Exception ex) when (IsFileError(ex)) { LoadWarnings.Add($"{source.Label}: {ex.Message}"); }
                    }
                    foreach (var generation in results.OrderBy(g => g.CreatedAt)) source.Generations.Add(generation);
                    source.SelectedGeneration = source.Generations.LastOrDefault();
                }
                sources.Add(source);
            }
            catch (Exception ex) when (IsFileError(ex)) { LoadWarnings.Add($"{manifest}: {ex.Message}"); }
        }
        return sources.OrderBy(s => s.CreatedAt).ToList();
    }

    public SourceEntry Import(string path, IEnumerable<SourceEntry> existing)
        => ImportImage(path, existing, new SourceEntry());

    public SourceEntry ImportGeneration(SourceEntry parent, GenerationEntry generation, IEnumerable<SourceEntry> existing)
    {
        if (!parent.Generations.Contains(generation)) throw new ArgumentException(Localization.Text("Результат не принадлежит исходнику."));
        string suffix = generation.IsAlignment ? "aligned" : generation.IsGridReduction ? "pixels" : "pixelized";
        return ImportImage(ResultPath(parent, generation), existing, new SourceEntry
        {
            DisplayName = $"{Path.GetFileNameWithoutExtension(parent.Label)}_{suffix}_{generation.Width}x{generation.Height}_{generation.CreatedAt:HHmmss}.png",
            AlignedCellSize = generation.IsGlobalAlignment ? generation.CellSize : generation.IsGridReduction ? 1 : 0,
            OriginSourceId = parent.Id, OriginGenerationId = generation.Id
        });
    }

    private SourceEntry ImportImage(string path, IEnumerable<SourceEntry> existing, SourceEntry source)
    {
        string original = Path.GetFullPath(path);
        using var stream = File.OpenRead(original);
        string fingerprint = Convert.ToHexString(SHA256.HashData(stream));
        var duplicate = existing.FirstOrDefault(s => string.Equals(s.OriginalPath, original, StringComparison.OrdinalIgnoreCase) && s.Fingerprint == fingerprint);
        if (duplicate is not null) return duplicate;
        stream.Position = 0;
        using var bitmap = new Bitmap(stream);
        source.OriginalPath = original;
        source.Fingerprint = fingerprint;
        source.Width = bitmap.Width;
        source.Height = bitmap.Height;
        Directory.CreateDirectory(SourceDirectory(source));
        SavePng(bitmap, SourcePath(source));
        source.Thumbnail = ReadThumbnail(SourcePath(source));
        WriteRecord(Path.Combine(SourceDirectory(source), "source.json"), source);
        return source;
    }

    public GenerationEntry SaveGeneration(SourceEntry source, Bitmap result, Bitmap? crop, GenerationEntry entry)
    {
        HistoryRecipes.Capture(source, entry);
        Directory.CreateDirectory(GenerationDirectory(source, entry));
        SavePng(result, ResultPath(source, entry));
        if (crop is not null) SavePng(crop, CropPath(source, entry));
        entry.Width = result.Width;
        entry.Height = result.Height;
        entry.HasCrop = crop is not null;
        entry.Thumbnail = ReadThumbnail(ResultPath(source, entry));
        WriteRecord(Path.Combine(GenerationDirectory(source, entry), "generation.json"), entry);
        return entry;
    }

    public void RemoveSource(SourceEntry source) => RemoveEntryDirectory(SourceDirectory(source));

    public SourceEntry Clone(SourceEntry source, Guid id, string name)
    {
        var copy = JsonSerializer.Deserialize<SourceEntry>(JsonSerializer.Serialize(source))!;
        copy.Id = id;
        copy.DisplayName = name;
        copy.OriginSourceId = source.Id;
        Directory.CreateDirectory(SourceDirectory(copy));
        File.Copy(SourcePath(source), SourcePath(copy));
        var ids = source.Generations.ToDictionary(g => g.Id, _ => Guid.NewGuid());
        foreach (var generation in source.Generations)
        {
            var item = JsonSerializer.Deserialize<GenerationEntry>(JsonSerializer.Serialize(generation))!;
            item.Id = ids[generation.Id];
            item.ParentGenerationId = generation.ParentGenerationId is { } parent && ids.TryGetValue(parent, out Guid newParent) ? newParent : null;
            Directory.CreateDirectory(GenerationDirectory(copy, item));
            File.Copy(ResultPath(source, generation), ResultPath(copy, item));
            if (generation.HasCrop) File.Copy(CropPath(source, generation), CropPath(copy, item));
            WriteRecord(Path.Combine(GenerationDirectory(copy, item), "generation.json"), item);
            item.Thumbnail = ReadThumbnail(ResultPath(copy, item));
            copy.Generations.Add(item);
        }
        copy.SelectedGeneration = copy.Generations.FirstOrDefault(g => source.SelectedGeneration is { } selected && g.Id == ids[selected.Id]);
        copy.Thumbnail = ReadThumbnail(SourcePath(copy));
        WriteRecord(Path.Combine(SourceDirectory(copy), "source.json"), copy);
        return copy;
    }

    public void RemoveGeneration(SourceEntry source, GenerationEntry generation)
    {
        if (!source.Generations.Contains(generation)) throw new ArgumentException(Localization.Text("Результат не принадлежит исходнику."));
        // Preserve any recoverable legacy chain before its only parent record disappears.
        foreach (var child in source.Generations.Where(g => g.ParentGenerationId == generation.Id && g != generation))
        {
            HistoryRecipes.Capture(source, child);
            WriteRecord(Path.Combine(GenerationDirectory(source, child), "generation.json"), child, overwrite: true);
        }
        RemoveEntryDirectory(GenerationDirectory(source, generation));
    }

    private void RemoveEntryDirectory(string directory)
    {
        string target = Path.GetFullPath(directory);
        string root = Path.TrimEndingDirectorySeparator(DirectoryPath) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Localization.Text("Удалять можно только записи библиотеки."));
        if (!Directory.Exists(target)) return;
        // Reject redirected library paths before moving or recursively deleting anything.
        for (var current = new DirectoryInfo(target); current is not null; current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException(Localization.Text("Запись библиотеки находится в перенаправленной папке."));
            if (string.Equals(Path.TrimEndingDirectorySeparator(current.FullName), Path.TrimEndingDirectorySeparator(DirectoryPath), StringComparison.OrdinalIgnoreCase)) break;
        }
        // Renaming commits deletion atomically. Load ignores these non-GUID directories,
        // so a locked image cannot leave a half-deleted visible history entry after restart.
        string removed = Path.Combine(Path.GetDirectoryName(target)!, ".deleted-" + Guid.NewGuid().ToString("N"));
        Directory.Move(target, removed);
        try { Directory.Delete(removed, recursive: true); }
        catch (IOException) { /* A locked snapshot can be cleaned up later; deletion is already committed. */ }
        catch (UnauthorizedAccessException) { }
    }

    public static Bitmap ReadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        using var loaded = new Bitmap(stream);
        // GDI+ clones may retain the decoder/stream. Copy ARGB bytes to owned memory
        // so visible images do not lock their files or depend on a disposed stream.
        var rectangle = new System.Drawing.Rectangle(0, 0, loaded.Width, loaded.Height);
        var copy = new Bitmap(loaded.Width, loaded.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            if (loaded.HorizontalResolution > 0 && loaded.VerticalResolution > 0)
                copy.SetResolution(loaded.HorizontalResolution, loaded.VerticalResolution);
            var input = loaded.LockBits(rectangle, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var output = copy.LockBits(rectangle, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    var row = new byte[checked(loaded.Width * 4)];
                    for (int y = 0; y < loaded.Height; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(input.Scan0, y * input.Stride), row, 0, row.Length);
                        System.Runtime.InteropServices.Marshal.Copy(row, 0, IntPtr.Add(output.Scan0, y * output.Stride), row.Length);
                    }
                }
                finally { copy.UnlockBits(output); }
            }
            finally { loaded.UnlockBits(input); }
            return copy;
        }
        catch { copy.Dispose(); throw; }
    }
    private static void SavePng(Bitmap bitmap, string path)
    {
        // GDI+ filename saves fail beyond MAX_PATH even when .NET can open the file.
        using var stream = File.Create(path);
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static BitmapSource ReadThumbnail(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (frame.PixelWidth <= 112 && frame.PixelHeight <= 112) { frame.Freeze(); return frame; }
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (frame.PixelWidth >= frame.PixelHeight) image.DecodePixelWidth = 112;
        else image.DecodePixelHeight = 112;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException(Localization.Text("Пустая запись истории."));
    private static void WriteRecord<T>(string path, T entry, bool overwrite = false)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, entry, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException;
}
