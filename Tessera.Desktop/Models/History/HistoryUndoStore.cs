using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tessera.Desktop;

internal sealed class HistorySnapshot
{
    public Guid SourceId { get; set; }
    public Guid? SelectedGenerationId { get; set; }
    public BranchPresentation Branch { get; set; } = new();
    public List<HistorySnapshotFile> Files { get; set; } = [];
    internal bool IncludesSourceImage => Files.Any(file => file.Name == "source.png");
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
    private readonly HistoryTrayStore _trayStore;
    private int _capacity;

    public HistoryUndoStore(string libraryPath, int capacity)
    {
        _libraryPath = Path.GetFullPath(libraryPath);
        _root = Path.Combine(_libraryPath, ".history-undo");
        _objects = Path.Combine(_root, "objects");
        _actions = Path.Combine(_root, "actions");
        _pending = Path.Combine(_root, "pending");
        _trayStore = new HistoryTrayStore(_libraryPath);
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

    public HistoryUndoTransaction Begin(SourceEntry source, BranchPresentation branch, Guid? exclude = null, bool includeSourceImage = false)
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
            if (includeSourceImage)
            {
                string folder = Path.GetDirectoryName(generations)!;
                foreach (string name in new[] { "source.png", "source.json" })
                    CaptureFile(snapshot, Path.Combine(folder, name), name);
            }
            if (Directory.Exists(generations))
            {
                foreach (string folder in Directory.EnumerateDirectories(generations))
                {
                    if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out Guid id) || id == exclude) continue;
                    foreach (string name in new[] { "result.png", "crop.png", "generation.json" })
                    {
                        string file = Path.Combine(folder, name);
                        if (File.Exists(file)) CaptureFile(snapshot, file, id.ToString("N") + "/" + name);
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

    private void CaptureFile(HistorySnapshot snapshot, string file, string name)
    {
        using var input = File.OpenRead(file);
        string hash = Convert.ToHexString(SHA256.HashData(input));
        string target = Path.Combine(_objects, hash);
        if (!File.Exists(target))
        {
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.Copy(file, temporary); File.Move(temporary, target, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        snapshot.Files.Add(new HistorySnapshotFile { Name = name, Hash = hash });
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
        try
        {
            foreach (var file in snapshot.Files)
            {
                string[] parts = file.Name.Split('/');
                bool sourceFile = file.Name is "source.png" or "source.json";
                bool generationFile = parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out _) &&
                    parts[1] is "result.png" or "crop.png" or "generation.json";
                if ((!sourceFile && !generationFile) || file.Hash.Length != 64 || !file.Hash.All(Uri.IsHexDigit))
                    throw new InvalidDataException("Invalid history undo snapshot.");
                string target = Path.Combine(stage, file.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(_objects, file.Hash), target);
            }
            if (snapshot.IncludesSourceImage)
            {
                foreach (string name in new[] { "source.png", "source.json" })
                {
                    var file = snapshot.Files.SingleOrDefault(file => file.Name == name)
                        ?? throw new InvalidDataException("Missing source file in history undo snapshot.");
                    string target = Path.Combine(parent, name);
                    string staged = Path.Combine(stage, name);
                    if (MatchesSnapshot(target, file.Hash)) File.Delete(staged);
                    else File.Move(staged, target, overwrite: true);
                }
            }
            if (Directory.Exists(current)) Directory.Move(current, previous);
            try { Directory.Move(stage, current); }
            catch { if (Directory.Exists(previous) && !Directory.Exists(current)) Directory.Move(previous, current); throw; }
            if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);
            _trayStore.SaveBranch(snapshot.SourceId, snapshot.Branch);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
    }

    private static bool MatchesSnapshot(string path, string hash)
    {
        if (!File.Exists(path)) return false;
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }

    private static HistorySnapshot ReadSnapshot(string directory)
        => JsonSerializer.Deserialize<HistorySnapshot>(File.ReadAllText(Path.Combine(directory, "snapshot.json")))
            ?? throw new InvalidDataException("Empty history undo snapshot.");

    private static void WriteSnapshot(string directory, HistorySnapshot snapshot)
    {
        string temporary = Path.Combine(directory, "snapshot.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
        File.Move(temporary, Path.Combine(directory, "snapshot.json"));
    }

    private void Commit(string path)
    {
        Directory.CreateDirectory(_actions);
        Directory.Move(path, Path.Combine(_actions, Path.GetFileName(path)));
        Prune();
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
