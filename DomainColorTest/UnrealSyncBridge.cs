using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DomainColorTest;

public sealed class UnrealSyncBridge
{
    private sealed class Settings
    {
        public string? ProjectPath { get; set; }
        public Dictionary<string, string> Received { get; set; } = [];
    }

    public sealed class Transfer
    {
        public string Id { get; set; } = "";
        public string AssetName { get; set; } = "";
        public string Kind { get; set; } = "color";
        public string Sha256 { get; set; } = "";
        public string Revision { get; set; } = "";
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly Regex AssetNamePattern = new("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant);
    private readonly string _settingsPath;
    private Settings _settings;

    public string? ProjectPath => _settings.ProjectPath;
    public string BridgeDirectory => Path.Combine(Path.GetDirectoryName(ProjectPath ?? throw new InvalidOperationException("Сначала выберите проект Unreal."))!, "Saved", "TesseraSync");

    public UnrealSyncBridge(string settingsPath)
    {
        _settingsPath = settingsPath;
        try { _settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath), JsonOptions) ?? new(); }
        catch (IOException) { _settings = new(); }
        catch (JsonException) { _settings = new(); }
    }

    public void SelectProject(string path)
    {
        string full = Path.GetFullPath(path);
        if (!File.Exists(full) || !string.Equals(Path.GetExtension(full), ".uproject", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Выберите существующий файл .uproject.", nameof(path));
        if (!string.Equals(_settings.ProjectPath, full, StringComparison.OrdinalIgnoreCase)) _settings.Received.Clear();
        _settings.ProjectPath = full;
        SaveSettings();
    }

    public void InstallPlugin()
    {
        string project = Path.GetDirectoryName(ProjectPath ?? throw new InvalidOperationException("Сначала выберите проект Unreal."))!;
        string target = Path.Combine(project, "Plugins", "TesseraSync");
        foreach (string relative in new[] { "TesseraSync.uplugin", "Content/Python/init_unreal.py", "Content/Python/tessera_sync.py" })
        {
            using var source = typeof(UnrealSyncBridge).Assembly.GetManifestResourceStream("Tessera.Unreal." + relative)
                ?? throw new InvalidOperationException("Не найден файл плагина TesseraSync: " + relative);
            string destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = File.Create(temporary))
                    source.CopyTo(output);
                File.Move(temporary, destination, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    public string SendPng(string pngPath, Guid id, string displayName, string kind = "color")
    {
        if (!File.Exists(pngPath) || !string.Equals(Path.GetExtension(pngPath), ".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Для Unreal нужен существующий PNG.", nameof(pngPath));
        if (kind is not ("color" or "height" or "normal" or "ao")) throw new ArgumentOutOfRangeException(nameof(kind));
        string name = MakeAssetName(displayName, id, kind);
        string directory = Path.Combine(BridgeDirectory, "to_unreal");
        Directory.CreateDirectory(directory);
        string output = Path.Combine(directory, name + ".png");
        string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.Copy(pngPath, temporary); File.Move(temporary, output, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        var transfer = new Transfer { Id = id.ToString("N") + ":" + kind, AssetName = name, Kind = kind, Sha256 = Hash(output), Revision = Guid.NewGuid().ToString("N") };
        WriteJson(Path.Combine(directory, name + ".json"), transfer);
        return "/Game/TesseraSync/" + name;
    }

    public IReadOnlyList<(Transfer Transfer, string Path)> PendingFromUnreal()
    {
        string directory = Path.Combine(BridgeDirectory, "to_tessera");
        if (!Directory.Exists(directory)) return [];
        var pending = new List<(Transfer, string)>();
        foreach (string json in Directory.EnumerateFiles(directory, "*.json"))
        {
            Transfer? transfer;
            try { transfer = JsonSerializer.Deserialize<Transfer>(File.ReadAllText(json), JsonOptions); }
            catch (IOException) { continue; }
            catch (JsonException) { continue; }
            if (transfer is null || !AssetNamePattern.IsMatch(transfer.AssetName) || Path.GetFileNameWithoutExtension(json) != transfer.AssetName || transfer.Sha256.Length != 64) continue;
            string png = Path.Combine(directory, transfer.AssetName + ".png");
            if (!File.Exists(png) || !string.Equals(Hash(png), transfer.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
            string key = ProjectPath + "|" + transfer.AssetName;
            if (_settings.Received.TryGetValue(key, out var received) && string.Equals(received, transfer.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
            pending.Add((transfer, png));
        }
        return pending;
    }

    public void MarkReceived(Transfer transfer)
    {
        _settings.Received[ProjectPath + "|" + transfer.AssetName] = transfer.Sha256;
        SaveSettings();
    }

    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string MakeAssetName(string displayName, Guid id, string kind)
    {
        string stem = Path.GetFileNameWithoutExtension(displayName);
        stem = Regex.Replace(stem, "[^A-Za-z0-9_]+", "_").Trim('_');
        if (stem.Length == 0 || !char.IsLetter(stem[0])) stem = "Image_" + stem;
        if (stem.Length > 20) stem = stem[..20].TrimEnd('_');
        string suffix = kind == "color" ? "" : "_" + kind;
        return "T_" + stem + "_" + id.ToString("N") + suffix;
    }

    private void SaveSettings() => WriteJson(_settingsPath, _settings);

    private static void WriteJson<T>(string path, T data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(data, JsonOptions)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
