using System.IO;
using System.Text.Json;

namespace DomainColorTest;

public sealed class ReleaseNote
{
    public string Version { get; set; } = "";
    public Dictionary<string, string> Title { get; set; } = [];
    public Dictionary<string, string> Notes { get; set; } = [];
    public string Heading => "v" + Version + " · " + InLanguage(Title);
    public string Body => InLanguage(Notes);

    private static string InLanguage(Dictionary<string, string> values) =>
        values.GetValueOrDefault(Localization.Language) ?? values.GetValueOrDefault("en") ?? "";
}

public sealed class ReleaseNotesCatalog
{
    public List<ReleaseNote> Releases { get; set; } = [];
    public static ReleaseNotesCatalog Bundled { get; } = LoadBundled();
    public ReleaseNote Current => Releases[0];

    public static ReleaseNotesCatalog Parse(string json, Version currentVersion)
    {
        var catalog = JsonSerializer.Deserialize<ReleaseNotesCatalog>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The release notes catalog is empty.");
        if (catalog.Releases is null || catalog.Releases.Count == 0) throw new InvalidDataException("The release notes catalog is empty.");
        var versions = new HashSet<string>();
        foreach (var release in catalog.Releases)
        {
            if (release is null) throw new InvalidDataException("The release notes catalog contains an empty entry.");
            if (release.Version == "next") release.Version = currentVersion.ToString(3);
            if (!Version.TryParse(release.Version, out var version) || version.Build < 0 || !versions.Add(release.Version) ||
                release.Title is null || release.Notes is null ||
                !release.Title.TryGetValue("en", out string? title) || string.IsNullOrWhiteSpace(title) ||
                !release.Notes.TryGetValue("en", out string? notes) || string.IsNullOrWhiteSpace(notes))
                throw new InvalidDataException("Invalid or duplicate release notes: " + release.Version);
        }
        if (catalog.Current.Version != currentVersion.ToString(3))
            throw new InvalidDataException("Release notes do not match the application version.");
        return catalog;
    }

    private static ReleaseNotesCatalog LoadBundled()
    {
        using var stream = typeof(ReleaseNotesCatalog).Assembly.GetManifestResourceStream("Tessera.ReleaseNotes.json")
            ?? throw new FileNotFoundException("Bundled release notes are missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), GitHubUpdater.CurrentVersion);
    }
}

public sealed class ReleaseNotesState(string path)
{
    public bool HasSeen(string version) => Read().Contains(version);

    public bool MarkSeen(string version)
    {
        var versions = Read();
        if (!versions.Add(version)) return true;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(versions));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private HashSet<string> Read()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
}
