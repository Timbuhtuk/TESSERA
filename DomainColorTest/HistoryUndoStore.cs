using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace DomainColorTest;

internal sealed class HistorySnapshot
{
    public Guid SourceId { get; set; }
    public Guid? SelectedGenerationId { get; set; }
    public BranchPresentation Branch { get; set; } = new();
    public List<HistorySnapshotFile> Files { get; set; } = [];
}

internal sealed class HistorySnapshotFile
{
    public string Name { get; set; } = "";
    public string Hash { get; set; } = "";
}

/// <summary>Bounded, persistent undo snapshots with content-addressed files.</summary>
internal sealed class HistoryUndoStore
{
    private readonly string _libraryPath;
    private readonly string _root;
    private readonly string _objects;
    private readonly string _actions;
    private readonly string _pending;
    private int _capacity;

    public HistoryUndoStore(string libraryPath, int capacity)
    {
        _libraryPath = Path.GetFullPath(libraryPath);
        _root = Path.Combine(_libraryPath, ".history-undo");
        _objects = Path.Combine(_root, "objects");
        _actions = Path.Combine(_root, "actions");
        _pending = Path.Combine(_root, "pending");
        _capacity = Math.Clamp(capacity, 1, 64);
    }

    public bool CanUndo => Directory.Exists(_actions) && Directory.EnumerateDirectories(_actions).Any();

    public void SetCapacity(int capacity)
    {
        _capacity = Math.Clamp(capacity, 1, 64);
        Prune();
    }

    public void RecoverPending()
    {
        if (!Directory.Exists(_pending)) return;
        foreach (string path in Directory.EnumerateDirectories(_pending).OrderBy(path => path))
        {
            if (!File.Exists(Path.Combine(path, "snapshot.json"))) { Directory.Delete(path, recursive: true); continue; }
            var snapshot = ReadSnapshot(path);
            Restore(snapshot);
            Directory.Delete(path, recursive: true);
        }
        CollectUnusedObjects();
    }

    public HistoryUndoTransaction Begin(SourceEntry source, BranchPresentation branch, Guid? exclude = null)
    {
        Directory.CreateDirectory(_objects);
        Directory.CreateDirectory(_pending);
        string path = Path.Combine(_pending, DateTime.UtcNow.Ticks.ToString("D19") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            var snapshot = new HistorySnapshot
            {
                SourceId = source.Id,
                SelectedGenerationId = source.SelectedGeneration?.Id,
                Branch = JsonSerializer.Deserialize<BranchPresentation>(JsonSerializer.Serialize(branch)) ?? new()
            };
            string generations = GenerationsPath(source.Id);
            if (Directory.Exists(generations))
            {
                foreach (string folder in Directory.EnumerateDirectories(generations))
                {
                    if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out Guid id) || id == exclude) continue;
                    foreach (string name in new[] { "result.png", "crop.png", "generation.json" })
                    {
                        string file = Path.Combine(folder, name);
                        if (!File.Exists(file)) continue;
                        using var input = File.OpenRead(file);
                        string hash = Convert.ToHexString(SHA256.HashData(input));
                        string target = Path.Combine(_objects, hash);
                        if (!File.Exists(target))
                        {
                            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                            try { File.Copy(file, temporary); File.Move(temporary, target, overwrite: true); }
                            finally { if (File.Exists(temporary)) File.Delete(temporary); }
                        }
                        snapshot.Files.Add(new HistorySnapshotFile { Name = id.ToString("N") + "/" + name, Hash = hash });
                    }
                }
            }
            WriteSnapshot(path, snapshot);
            return new HistoryUndoTransaction(this, path, snapshot);
        }
        catch
        {
            Directory.Delete(path, recursive: true);
            throw;
        }
    }

    public HistorySnapshot? Undo()
    {
        if (!CanUndo) return null;
        string action = Directory.EnumerateDirectories(_actions).OrderByDescending(path => path).First();
        var snapshot = ReadSnapshot(action);
        Restore(snapshot);
        Directory.Delete(action, recursive: true);
        CollectUnusedObjects();
        return snapshot;
    }

    private string GenerationsPath(Guid sourceId) => Path.Combine(_libraryPath, sourceId.ToString("N"), "generations");

    private void Restore(HistorySnapshot snapshot)
    {
        string current = GenerationsPath(snapshot.SourceId);
        string parent = Path.GetDirectoryName(current)!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        string stage = Path.Combine(parent, ".history-restore-" + Guid.NewGuid().ToString("N"));
        string previous = Path.Combine(parent, ".history-old-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        string branchPath = Path.Combine(_libraryPath, "editor-history.json");
        string branchTemporary = branchPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            foreach (var file in snapshot.Files)
            {
                string[] parts = file.Name.Split('/');
                if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out _) ||
                    parts[1] is not ("result.png" or "crop.png" or "generation.json") ||
                    file.Hash.Length != 64 || !file.Hash.All(Uri.IsHexDigit))
                    throw new InvalidDataException("Invalid history undo snapshot.");
                string target = Path.Combine(stage, parts[0], parts[1]);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(_objects, file.Hash), target);
                using var copied = File.OpenRead(target);
                if (!Convert.ToHexString(SHA256.HashData(copied)).Equals(file.Hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(Localization.Text("Снимок истории повреждён. Текущие результаты сохранены."));
            }
            // Read and prepare branch state before replacing any visible result.
            WriteRestoredBranch(branchPath, branchTemporary, snapshot);
            if (Directory.Exists(current)) Directory.Move(current, previous);
            bool published = false;
            try
            {
                Directory.Move(stage, current); published = true;
                File.Move(branchTemporary, branchPath, overwrite: true);
            }
            catch
            {
                if (published) Directory.Move(current, stage);
                if (Directory.Exists(previous)) Directory.Move(previous, current);
                throw;
            }
            // Both visible records are committed. Cleanup cannot turn that into a failed undo.
            DeleteCommittedDirectory(previous);
        }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            if (File.Exists(branchTemporary)) File.Delete(branchTemporary);
        }
    }

    private static void WriteRestoredBranch(string path, string temporary, HistorySnapshot snapshot)
    {
        HistoryTrayState state;
        try { state = File.Exists(path) ? JsonSerializer.Deserialize<HistoryTrayState>(File.ReadAllText(path)) ?? new() : new(); }
        catch (JsonException) { state = new(); }
        state.Branches[snapshot.SourceId] = snapshot.Branch;
        File.WriteAllText(temporary, JsonSerializer.Serialize(state));
    }

    private static void DeleteCommittedDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException ex) { System.Diagnostics.Trace.TraceWarning(ex.Message); }
        catch (UnauthorizedAccessException ex) { System.Diagnostics.Trace.TraceWarning(ex.Message); }
    }

    private static HistorySnapshot ReadSnapshot(string directory)
        => JsonSerializer.Deserialize<HistorySnapshot>(File.ReadAllText(Path.Combine(directory, "snapshot.json")))
            ?? throw new InvalidDataException("Empty history undo snapshot.");

    private static void WriteSnapshot(string directory, HistorySnapshot snapshot)
    {
        string path = Path.Combine(directory, "snapshot.json");
        string temporary = path + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot)); File.Move(temporary, path); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void Commit(string path)
    {
        Directory.CreateDirectory(_actions);
        Directory.Move(path, Path.Combine(_actions, Path.GetFileName(path)));
        try { Prune(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { System.Diagnostics.Trace.TraceWarning(ex.Message); }
    }

    private void Prune()
    {
        if (!Directory.Exists(_actions)) return;
        foreach (string path in Directory.EnumerateDirectories(_actions).OrderByDescending(path => path).Skip(_capacity))
            Directory.Delete(path, recursive: true);
        CollectUnusedObjects();
    }

    private void CollectUnusedObjects()
    {
        if (!Directory.Exists(_objects)) return;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in new[] { _actions, _pending })
            if (Directory.Exists(root))
                foreach (string path in Directory.EnumerateDirectories(root))
                    if (File.Exists(Path.Combine(path, "snapshot.json")))
                    foreach (var file in ReadSnapshot(path).Files) used.Add(file.Hash);
        foreach (string path in Directory.EnumerateFiles(_objects))
            if (!used.Contains(Path.GetFileName(path))) File.Delete(path);
    }

    internal sealed class HistoryUndoTransaction(HistoryUndoStore owner, string path, HistorySnapshot snapshot)
    {
        private bool _finished;
        public void Commit() { if (_finished) return; owner.Commit(path); _finished = true; }
        public HistorySnapshot Rollback()
        {
            if (_finished) throw new InvalidOperationException("History transaction already committed.");
            owner.Restore(snapshot);
            Directory.Delete(path, recursive: true);
            _finished = true;
            owner.CollectUnusedObjects();
            return snapshot;
        }
    }
}
