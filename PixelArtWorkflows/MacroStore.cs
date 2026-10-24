using System.Text.Json;

namespace PixelArtWorkflows;

public sealed class MacroStore(string libraryDirectory)
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetFullPath(libraryDirectory), "macros");
    public List<string> LoadWarnings { get; } = [];
    private readonly Dictionary<Guid, string> _files = [];

    public IReadOnlyList<MacroDefinition> List()
    {
        LoadWarnings.Clear();
        _files.Clear();
        if (!Directory.Exists(DirectoryPath)) return [];
        var result = new List<MacroDefinition>();
        foreach (string file in Directory.EnumerateFiles(DirectoryPath, "*.json"))
        {
            try
            {
                var macro = Read(file);
                if (!_files.TryAdd(macro.Id, file)) throw new InvalidDataException($"Duplicate macro identifier: {macro.Id}.");
                result.Add(macro);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
            { LoadWarnings.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
        }
        return result.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static MacroDefinition Read(string file)
    {
        string json = File.ReadAllText(file);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.EnumerateObject().Any(p => p.Name.Equals("version", StringComparison.OrdinalIgnoreCase)) ||
            !document.RootElement.EnumerateObject().Any(p => p.Name.Equals("id", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("A macro needs an explicit version and identifier.");
        var macro = JsonSerializer.Deserialize<MacroDefinition>(json, WorkflowJson.Options)
            ?? throw new InvalidDataException("Empty macro.");
        Validate(macro);
        return macro;
    }

    public static void Validate(MacroDefinition macro)
    {
        if (macro.Version != 1) throw new InvalidDataException($"Unsupported macro version: {macro.Version}.");
        if (string.IsNullOrWhiteSpace(macro.Name)) throw new InvalidDataException("A macro needs a name.");
        if (macro.Id == Guid.Empty) throw new InvalidDataException("A macro needs an identifier.");
        if (macro.Steps is null || macro.Steps.Count == 0) throw new InvalidDataException("A macro needs at least one step.");
    }

    public MacroDefinition Save(MacroDefinition macro)
    {
        Validate(macro);
        string name = macro.Name.Trim();
        if (List().Any(m => m.Id != macro.Id && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A macro with this name already exists.");
        var saved = WorkflowJson.Snapshot(new MacroDefinition { Id = macro.Id, Name = name, Steps = macro.Steps });
        Directory.CreateDirectory(DirectoryPath);
        string path = PathFor(saved.Id), temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(saved, WorkflowJson.Options));
        File.Move(temporary, path, true);
        return saved;
    }

    public void Delete(Guid id) { List(); File.Delete(PathFor(id)); }
    public string PathFor(Guid id) => _files.GetValueOrDefault(id) ?? Path.Combine(DirectoryPath, id.ToString("N") + ".json");
}
