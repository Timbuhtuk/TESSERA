using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace Tessera.Desktop;

public sealed class HistoryTrayState
{
    public int Version { get; set; }
    public double Height { get; set; } = 120;
    public double ExpandedHeight { get; set; } = 290;
    public Dictionary<Guid, Guid?> CurrentFolders { get; set; } = [];
    public HashSet<Guid> CollapsedFolders { get; set; } = [];
}

internal sealed class HistoryTrayStore(string root)
{
    private const int CurrentVersion = 2;
    private readonly string _path = Path.Combine(root, "editor-history.json");
    private readonly string _records = Path.Combine(root, ".editor-history");
    private bool _initialized;

    public HistoryTrayState Load()
    {
        if (!File.Exists(_path))
        {
            _initialized = true;
            return new HistoryTrayState { Version = CurrentVersion };
        }

        var state = LibraryJson.Read<HistoryTrayState>(_path);
        if (state.Version != CurrentVersion)
            throw new InvalidDataException("Unsupported editor history version.");
        _initialized = true;
        return state;
    }

    public void SavePreferences(HistoryTrayState state)
    {
        EnsureInitialized();
        state.Version = CurrentVersion;
        LibraryJson.Write(_path, state);
    }

    public BranchPresentation ReadBranch(Guid source)
    {
        string path = BranchPath(source);
        return File.Exists(path) ? LibraryJson.Read<BranchPresentation>(path) : new();
    }

    public void SaveBranch(Guid source, BranchPresentation branch)
    {
        try { EnsureInitialized(); }
        catch (JsonException) { } // The branch can still be restored by undo if panel settings are unreadable.
        WriteBranch(source, branch);
    }

    public Point ReadScroll(string key)
    {
        string path = ScrollPath(key);
        return File.Exists(path) ? LibraryJson.Read<Point>(path) : default;
    }

    public void SaveScroll(string key, Point position)
    {
        EnsureInitialized();
        WriteScroll(key, position);
    }

    private void EnsureInitialized()
    {
        if (!_initialized) Load();
    }

    private void WriteScroll(string key, Point position)
    {
        string path = ScrollPath(key);
        if (position != default) LibraryJson.Write(path, position);
        else if (File.Exists(path)) File.Delete(path);
    }

    private void WriteBranch(Guid source, BranchPresentation branch)
    {
        string path = BranchPath(source);
        if (branch.Roots.Count > 0 || branch.Detached.Count > 0) LibraryJson.Write(path, branch);
        else if (File.Exists(path)) File.Delete(path);
    }

    private string BranchPath(Guid source) => Path.Combine(_records, "branches", source.ToString("N") + ".json");

    private string ScrollPath(string key)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_records, "scroll", hash + ".json");
    }
}
