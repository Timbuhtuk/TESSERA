using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using Tessera.Core.Imaging;
using Tessera.Core.Alignment;
using Tessera.Core.Workflows;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

public sealed class EditorSettings
{
    public int Mode { get; set; } = 1;
    public DownscaleOptions Downscale { get; set; } = new();
    public ConnectivityPaletteOptions? ConnectedPalette { get; set; }
    public ChannelAdjustmentOptions ChannelAdjustment { get; set; } = new();
    public int CellSize { get; set; } = 8;
    public bool DetectGrid { get; set; }
    // TESSERA_LEGACY History without alignment mode or snap settings uses the original global, unsnapped grid.
    public EGridAlignmentMode AlignmentMode { get; set; } = EGridAlignmentMode.Global;
    public bool SnapToGlobalGrid { get; set; }
    public int ContourWindowSize { get; set; } = 15;
    public ContourCleanStrictness ContourStrictness { get; set; } = ContourCleanStrictness.Strong;
    public bool ContourProtectDetails { get; set; }
    public int ContourSimilarColorDistance { get; set; } = 24;
    public int ContourMinimumPixelGradient { get; set; } = 120;
    public int ContourMinimumNeighborGradient { get; set; } = 80;
    public int ContourMinimumSimilarNeighbors { get; set; } = 2;
    public int ContourMinimumAgreement { get; set; } = 35;
    public int ContourMinimumOpposition { get; set; } = 10;
    public bool AspectLock { get; set; }
}

public sealed class SourceEntry : INotifyPropertyChanged
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
    [JsonIgnore] public string RemoveDescription => Localization.Format("RemoveSourceItsResultsAppFormat", Label);
    private BitmapSource? _thumbnail;
    internal ThumbnailResource? ThumbnailFile { get; set; }
    internal ThumbnailResource? PreviewFor(Guid? resultId)
    {
        if (ThumbnailFile is not { } source || resultId is not { } result) return ThumbnailFile;
        return source with
        {
            Path = Path.Combine(Path.GetDirectoryName(source.Path)!, "generations", result.ToString("N"), "result.png"),
            Fallback = source.Path
        };
    }
    [JsonIgnore] public BitmapSource? Thumbnail { get => ThumbnailFile?.Get() ?? _thumbnail; set => _thumbnail = value; }
    private ObservableCollection<GenerationEntry>? _generations;
    private Func<List<GenerationEntry>>? _readHistory;
    private GenerationEntry? _selectedGeneration;
    private bool _hasSelection;
    internal bool HasLoadedHistory => _generations is not null;
    internal Guid? SelectedResultId { get; private set; }
    [JsonIgnore]
    public ObservableCollection<GenerationEntry> Generations
    {
        get
        {
            if (_generations is not null) return _generations;
            _generations = new ObservableCollection<GenerationEntry>(_readHistory?.Invoke() ?? []);
            SelectedGeneration = _hasSelection ? _generations.FirstOrDefault(entry => entry.Id == SelectedResultId) : _generations.LastOrDefault();
            _generations.CollectionChanged += HistoryChanged;
            return _generations;
        }
    }
    [JsonIgnore]
    public GenerationEntry? SelectedGeneration
    {
        get { _ = Generations; return _selectedGeneration; }
        set { _selectedGeneration = value; SelectedResultId = value?.Id; _hasSelection = true; }
    }
    [JsonIgnore] public EditorSettings? DraftSettings { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event NotifyCollectionChangedEventHandler? GenerationsChanged;

    internal void SetHistoryLoader(Func<List<GenerationEntry>> readHistory) => _readHistory = readHistory;

    internal void RestoreSelection(Guid? id)
    {
        SelectedResultId = id;
        _hasSelection = true;
        _selectedGeneration = _generations?.FirstOrDefault(entry => entry.Id == id);
    }

    internal void UnloadHistory()
    {
        if (_readHistory is null || _generations is null) return;
        _generations.CollectionChanged -= HistoryChanged;
        _generations = null;
        _selectedGeneration = null;
    }

    private void HistoryChanged(object? sender, NotifyCollectionChangedEventArgs args) => GenerationsChanged?.Invoke(this, args);

    internal void UpdateImageDetails(int width, int height, int alignedCellSize)
    {
        Width = width;
        Height = height;
        AlignedCellSize = alignedCellSize;
        PropertyChanged?.Invoke(this, new(nameof(Thumbnail)));
        PropertyChanged?.Invoke(this, new(nameof(Description)));
    }
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
        set { if (_presentationHint == value) return; _presentationHint = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PresentationHint))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResultTreeDescription))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    [JsonIgnore] public bool IsHeader => false;
    [JsonIgnore] public bool IsGlobalAlignment => IsAlignment && (Settings.AlignmentMode == EGridAlignmentMode.Global || Settings.SnapToGlobalGrid);
    [JsonIgnore] public string? IconOperationId
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(OperationId)) return OperationId;
            if (AppliedRecipe?.Steps is [var step] && !string.IsNullOrWhiteSpace(step.OperationId)) return step.OperationId;
            // TESSERA_LEGACY Choose result-card icons for history records that have settings or old labels instead of an operation ID.
            if (IsAlignment && Settings.AlignmentMode == EGridAlignmentMode.Local)
                return Settings.SnapToGlobalGrid ? ProcessingOperations.AlignLocalSnapped : ProcessingOperations.AlignLocal;
            return HistoryRecipes.LegacyOperationId(this) ?? (Operation switch
            {
                "Замена цвета" or "Color replacement" => ProcessingOperations.ReplaceColor,
                "Локальные цвета" or "Local colors" or "Сглаживание" or "Smoothing" => ProcessingOperations.Cleanup,
                "Палитра · связный отбор" or "Palette · connected selection" => ProcessingOperations.Colors,
                "Обрезка прозрачных краёв" or "Trim transparent edges" => ProcessingOperations.Trim,
                "Удаление фона" or "Background removal" => ProcessingOperations.RemoveBackground,
                "Резкость" or "Sharpen" => ProcessingOperations.Sharpen,
                "ASCII" => ProcessingOperations.Ascii,
                _ => null
            });
        }
    }
    [JsonIgnore] public string Label => IsAlignment ? Localization.Format(Settings.AlignmentMode == EGridAlignmentMode.Local
        ? Settings.SnapToGlobalGrid ? "SnappedLocalGridPxFormat" : "LocalGridPxFormat"
        : "GridPxFormat", CellSize, CreatedAt)
        : $"{(Operation.Length > 0 ? (OperationId == ProcessingOperations.Macro ? Operation : Localization.StoredText(Operation)) + " · " : IsGridReduction ? "1 px · " : "")}{Width} × {Height} · {CreatedAt:HH:mm:ss}";
    [JsonIgnore] public string ToolTipOperation => IsAlignment
        ? Localization.Text(Settings.AlignmentMode == EGridAlignmentMode.Local
            ? Settings.SnapToGlobalGrid ? "LocalGridWithSnapping" : "LocalGrid" : "GlobalGrid")
        : IsGridReduction ? Localization.Text("GridReduction")
        : Operation.Length > 0 ? OperationId == ProcessingOperations.Macro ? Operation : Localization.StoredText(Operation)
        : Localization.Text(OperationId == ProcessingOperations.Profile ? "ImageProcessing" : "ResultLabel");
    [JsonIgnore] public string ToolTipSize => Localization.Format("SizePxFormat", Width, Height);
    [JsonIgnore] public string ToolTipCreated => Localization.Format("CreatedFormat", CreatedAt);
    [JsonIgnore] public string ToolTipDetails
    {
        get
        {
            string details = Localization.StoredText(Log).Trim();
            string repeatedHeader = $"{ToolTipOperation}: {Width} × {Height} px; ";
            return details.StartsWith(repeatedHeader, StringComparison.OrdinalIgnoreCase) ? details[repeatedHeader.Length..] : details;
        }
    }
    [JsonIgnore] public string Description => string.Join("\n", new[] { ToolTipOperation, ToolTipSize, ToolTipCreated, PresentationHint, ToolTipDetails }.Where(part => !string.IsNullOrWhiteSpace(part)));
    [JsonIgnore] public string ResultTreeDescription => Description;
    [JsonIgnore] public string RemoveDescription => Localization.Format("RemoveResultFromHistoryFormat", Label);
    private BitmapSource? _thumbnail;
    internal ThumbnailResource? ThumbnailFile { get; set; }
    [JsonIgnore] public BitmapSource? Thumbnail { get => ThumbnailFile?.Get() ?? _thumbnail; set => _thumbnail = value; }
}

/// <summary>Stored source images and result snapshots with their JSON records.</summary>
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
    internal ImageResources Resources { get; } = new();
    public List<string> LoadWarnings { get; } = [];
    public string SourcePath(SourceEntry source) => Path.Combine(SourceDirectory(source), "source.png");
    public void RenameSource(SourceEntry source, string name)
    {
        string? previous = source.DisplayName;
        source.DisplayName = name;
        try { WriteRecord(Path.Combine(SourceDirectory(source), "source.json"), source, overwrite: true); }
        catch { source.DisplayName = previous; throw; }
    }
    public string ResultPath(SourceEntry source, GenerationEntry generation) => ResultPath(source, generation.Id);
    internal string ResultPath(SourceEntry source, Guid generation) => Path.Combine(SourceDirectory(source), "generations", generation.ToString("N"), "result.png");
    public string CropPath(SourceEntry source, GenerationEntry generation) => Path.Combine(GenerationDirectory(source, generation), "crop.png");
    private string SourceDirectory(SourceEntry source) => Path.Combine(DirectoryPath, source.Id.ToString("N"));
    private string GenerationDirectory(SourceEntry source, GenerationEntry generation) => Path.Combine(SourceDirectory(source), "generations", generation.Id.ToString("N"));

    public IReadOnlyList<SourceEntry> Load() => Load(restoreHistories: true);
    internal IReadOnlyList<SourceEntry> LoadHeaders() => Load(restoreHistories: false);

    private IReadOnlyList<SourceEntry> Load(bool restoreHistories)
    {
        LoadWarnings.Clear();
        var sources = new List<SourceEntry>();
        if (!Directory.Exists(DirectoryPath)) return sources;
        foreach (string folder in Directory.EnumerateDirectories(DirectoryPath))
        {
            if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out Guid id)) continue;
            if (LoadSource(id, restoreHistories) is { } source) sources.Add(source);
        }
        return sources.OrderBy(s => s.CreatedAt).ToList();
    }

    internal SourceEntry? LoadSource(Guid id, bool restoreHistory = true)
    {
        string folder = Path.Combine(DirectoryPath, id.ToString("N"));
        string manifest = Path.Combine(folder, "source.json");
        if (!File.Exists(manifest)) return null;
        try
        {
            var source = Read<SourceEntry>(manifest);
            if (source.Id != id) throw new InvalidDataException(Localization.Text("SourceIdentifierDoesNotMatch"));
            if (!File.Exists(SourcePath(source))) throw new FileNotFoundException(null, SourcePath(source));
            AttachSource(source);
            if (restoreHistory) _ = source.Generations;
            return source;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            LoadWarnings.Add($"{manifest}: {ex.Message}");
            return null;
        }
    }

    internal List<GenerationEntry> ReadHistory(SourceEntry source)
    {
        var results = new List<GenerationEntry>();
        string generations = Path.Combine(SourceDirectory(source), "generations");
        if (!Directory.Exists(generations)) return results;
        foreach (string folder in Directory.EnumerateDirectories(generations))
        {
            if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out Guid id)) continue;
            if (ReadGeneration(source, id) is { } entry) results.Add(entry);
        }
        return results.OrderBy(entry => entry.CreatedAt).ToList();
    }

    internal GenerationEntry? FindGeneration(SourceEntry source, Guid id)
        => source.HasLoadedHistory ? source.Generations.FirstOrDefault(entry => entry.Id == id) : ReadGeneration(source, id);

    private GenerationEntry? ReadGeneration(SourceEntry source, Guid id)
    {
        string path = ResultPath(source, id);
        string record = Path.Combine(Path.GetDirectoryName(path)!, "generation.json");
        if (!File.Exists(record)) return null;
        try
        {
            var entry = Read<GenerationEntry>(record);
            if (entry.Id != id || entry.Settings?.Downscale is null) throw new InvalidDataException(Localization.Text("ResultSettingsAreCorrupted"));
            // TESSERA_LEGACY Validate old generation records before allowing macro reconstruction.
            HistoryRecipes.ValidateLegacyRecord(entry, record);
            if (!File.Exists(path)) throw new FileNotFoundException(null, path);
            if (entry.HasCrop && !File.Exists(CropPath(source, entry))) throw new InvalidDataException(Localization.Text("ProcessingAreaMissing"));
            entry.ThumbnailFile = new ThumbnailResource(Resources, path);
            return entry;
        }
        catch (Exception ex) when (IsFileError(ex)) { LoadWarnings.Add($"{source.Label}: {ex.Message}"); return null; }
    }

    private void AttachSource(SourceEntry source)
    {
        source.ThumbnailFile = new ThumbnailResource(Resources, SourcePath(source));
        source.SetHistoryLoader(() => ReadHistory(source));
    }

    public SourceEntry Import(string path, IEnumerable<SourceEntry> existing)
        => ImportImage(path, existing, new SourceEntry());

    public SourceEntry ImportGeneration(SourceEntry parent, GenerationEntry generation, IEnumerable<SourceEntry> existing)
    {
        if (!parent.Generations.Contains(generation)) throw new ArgumentException(Localization.Text("ResultDoesNotBelongThisSource"));
        string suffix = generation.IsAlignment ? "aligned" : generation.IsGridReduction ? "pixels" : "pixelized";
        return ImportImage(ResultPath(parent, generation), existing, new SourceEntry
        {
            DisplayName = $"{Path.GetFileNameWithoutExtension(parent.Label)}_{suffix}_{generation.Width}x{generation.Height}_{generation.CreatedAt:HHmmss}.png",
            AlignedCellSize = ResultAlignedCellSize(generation),
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
        _ = source.Generations;
        AttachSource(source);
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
        entry.ThumbnailFile = new ThumbnailResource(Resources, ResultPath(source, entry));
        WriteRecord(Path.Combine(GenerationDirectory(source, entry), "generation.json"), entry);
        return entry;
    }

    public void ReplaceGeneration(SourceEntry source, GenerationEntry entry, Bitmap result, Bitmap? crop = null)
    {
        string folder = GenerationDirectory(source, entry);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
        string png = Path.Combine(folder, "result.png");
        string temporary = png + ".tmp";
        try { SavePng(result, temporary); File.Move(temporary, png, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        entry.Width = result.Width;
        entry.Height = result.Height;
        string cropPath = Path.Combine(folder, "crop.png");
        if (entry.HasCrop && crop is null) throw new InvalidOperationException("Recomputed crop is missing.");
        if (crop is not null) SavePng(crop, cropPath);
        else if (File.Exists(cropPath)) File.Delete(cropPath);
        entry.HasCrop = crop is not null;
        Resources.Invalidate(png);
        entry.ThumbnailFile = new ThumbnailResource(Resources, png);
        WriteRecord(Path.Combine(folder, "generation.json"), entry, overwrite: true);
    }

    // The caller snapshots the source and history before changing their stored records.
    internal void ReplaceSourceImage(SourceEntry source, GenerationEntry generation)
    {
        string path = SourcePath(source);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(ResultPath(source, generation), temporary);
            using var bitmap = ReadBitmap(temporary);
            File.Move(temporary, path, overwrite: true);
            Resources.Invalidate(path);
            source.UpdateImageDetails(bitmap.Width, bitmap.Height, ResultAlignedCellSize(generation));
            WriteRecord(Path.Combine(SourceDirectory(source), "source.json"), source, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static int ResultAlignedCellSize(GenerationEntry entry)
        => entry.IsGlobalAlignment ? entry.CellSize : entry.IsGridReduction ? 1 : 0;

    internal void SaveGenerationRecord(SourceEntry source, GenerationEntry entry)
        => WriteRecord(Path.Combine(GenerationDirectory(source, entry), "generation.json"), entry, overwrite: true);

    public void DeleteUnpublishedGeneration(SourceEntry source, GenerationEntry entry)
        => DeleteGenerationFiles(source, entry);

    internal void DeleteGenerationFiles(SourceEntry source, GenerationEntry entry)
    {
        string path = GenerationDirectory(source, entry);
        RemoveEntryDirectory(path);
        Resources.InvalidateDirectory(path);
    }

    public void RemoveSource(SourceEntry source)
    {
        string path = SourceDirectory(source);
        RemoveEntryDirectory(path);
        Resources.InvalidateDirectory(path);
    }

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
            item.ThumbnailFile = new ThumbnailResource(Resources, ResultPath(copy, item));
            copy.Generations.Add(item);
        }
        copy.SelectedGeneration = copy.Generations.FirstOrDefault(g => source.SelectedGeneration is { } selected && g.Id == ids[selected.Id]);
        AttachSource(copy);
        WriteRecord(Path.Combine(SourceDirectory(copy), "source.json"), copy);
        return copy;
    }

    public void RemoveGeneration(SourceEntry source, GenerationEntry generation)
    {
        if (!source.Generations.Contains(generation)) throw new ArgumentException(Localization.Text("ResultDoesNotBelongThisSource"));
        // TESSERA_LEGACY Snapshot recoverable pre-recipe history while the ancestor records still exist.
        foreach (var child in source.Generations.Where(g => g.ParentGenerationId == generation.Id && g != generation))
        {
            HistoryRecipes.Capture(source, child);
            SaveGenerationRecord(source, child);
        }
        DeleteGenerationFiles(source, generation);
    }

    private void RemoveEntryDirectory(string directory)
    {
        string target = Path.GetFullPath(directory);
        string root = Path.TrimEndingDirectorySeparator(DirectoryPath) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Localization.Text("OnlyLibraryEntriesCanBeDeleted"));
        if (!Directory.Exists(target)) return;
        // Reject redirected library paths before moving or recursively deleting anything.
        for (var current = new DirectoryInfo(target); current is not null; current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException(Localization.Text("LibraryEntryRedirectedFolder"));
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

    private static T Read<T>(string path)
    {
        using var file = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(file, JsonOptions) ?? throw new InvalidDataException(Localization.Text("EmptyHistoryEntry"));
    }
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
