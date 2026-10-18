using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace DomainColorTest;

public enum WorkspaceAssetKind { Image, Animation, Board, Tiles }

public sealed class WorkspaceAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public WorkspaceAssetKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string ContentFile { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset OpenedAt { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool? Transparent { get; set; }
    public Guid? SelectedResult { get; set; }
    public EditorSettings? Draft { get; set; }
    public int Basis { get; set; }
    public int Zoom { get; set; }
    public int Background { get; set; }
    public int SheetLayout { get; set; }
    public int SheetColumns { get; set; } = 8;
    public int SheetPadding { get; set; }
    [JsonIgnore] public BitmapSource? Thumbnail { get; set; }
    [JsonIgnore] public string KindLabel => Kind switch { WorkspaceAssetKind.Animation => Localization.Text("Анимация"), WorkspaceAssetKind.Board => Localization.Text("Доска"), WorkspaceAssetKind.Tiles => Localization.Text("Коллекция тайлов"), _ => Localization.Text("Изображение") };
    [JsonIgnore] public string Summary => Width > 0 ? $"{KindLabel} · {Width} × {Height}" : KindLabel;
    [JsonIgnore] public string TagLabel => string.Join(" · ", Tags);
    [JsonIgnore] public string LocationLabel { get; set; } = "";
}

public sealed class WorkspaceProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
}

public sealed class WorkspaceFolder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = "";
}

public sealed class WorkspaceLink
{
    public Guid ProjectId { get; set; }
    public Guid AssetId { get; set; }
    public Guid? FolderId { get; set; }
}

public sealed class WorkspaceState
{
    public int Version { get; set; } = 1;
    public List<WorkspaceAsset> Assets { get; set; } = [];
    public List<WorkspaceProject> Projects { get; set; } = [];
    public List<WorkspaceFolder> Folders { get; set; } = [];
    public List<WorkspaceLink> Links { get; set; } = [];
    public string View { get; set; } = "all";
    public Guid? ProjectId { get; set; }
    public Guid? FolderId { get; set; }
    public Guid? SelectedAsset { get; set; }
    public string Search { get; set; } = "";
    public int Scope { get; set; }
    public int Kind { get; set; }
    public string Width { get; set; } = "";
    public string Height { get; set; } = "";
    public int Transparency { get; set; }
}

/// <summary>Projects contain references; document payloads and image histories belong to the library.</summary>
public sealed class WorkspaceCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string Root { get; }
    public WorkspaceState State { get; }
    public WorkspaceCatalog(string root)
    {
        Root = Path.GetFullPath(root);
        string path = Path.Combine(Root, "workspace.json");
        State = File.Exists(path) ? JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException(Localization.Text("Пустой каталог рабочего пространства.")) : new();
        if (State.Version != 1) throw new InvalidDataException(Localization.Text("Неизвестная версия рабочего пространства."));
        if (State.Assets.Select(a => a.Id).Distinct().Count() != State.Assets.Count)
            throw new InvalidDataException(Localization.Text("Повторяющиеся ассеты в каталоге."));
    }

    public void Save()
    {
        Directory.CreateDirectory(Root);
        string path = Path.Combine(Root, "workspace.json"), temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(State, JsonOptions));
        File.Move(temporary, path, true);
    }

    public void SyncImages(IEnumerable<SourceEntry> sources)
    {
        foreach (var source in sources)
        {
            var asset = State.Assets.FirstOrDefault(a => a.Id == source.Id);
            if (asset is null)
            {
                asset = new WorkspaceAsset { Id = source.Id, Name = source.Label, Width = source.Width, Height = source.Height };
                State.Assets.Add(asset);
            }
            asset.Thumbnail = source.SelectedGeneration?.Thumbnail ?? source.Thumbnail;
            var result = source.SelectedGeneration;
            asset.Width = result?.Width ?? source.Width;
            asset.Height = result?.Height ?? source.Height;
        }
    }

    public WorkspaceProject CreateProject(string name)
    {
        name = CleanName(name);
        if (State.Projects.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException(Localization.Text("Проект с таким названием уже есть."));
        var project = new WorkspaceProject { Name = name };
        State.Projects.Add(project); Save(); return project;
    }

    public WorkspaceFolder CreateFolder(Guid projectId, Guid? parentId, string name)
    {
        RequireProject(projectId); RequireFolder(projectId, parentId); name = CleanName(name);
        if (State.Folders.Any(f => f.ProjectId == projectId && f.ParentId == parentId && f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException(Localization.Text("Папка с таким названием уже есть."));
        var folder = new WorkspaceFolder { ProjectId = projectId, ParentId = parentId, Name = name };
        State.Folders.Add(folder); Save(); return folder;
    }

    public void Rename(Guid id, string name)
    {
        name = CleanName(name);
        if (State.Projects.FirstOrDefault(p => p.Id == id) is { } project)
        {
            if (State.Projects.Any(p => p.Id != id && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException(Localization.Text("Такой проект уже есть."));
            project.Name = name;
        }
        else if (State.Folders.FirstOrDefault(f => f.Id == id) is { } folder)
        {
            if (State.Folders.Any(f => f.Id != id && f.ProjectId == folder.ProjectId && f.ParentId == folder.ParentId && f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException(Localization.Text("Такая папка уже есть."));
            folder.Name = name;
        }
        else throw new ArgumentException(Localization.Text("Раздел не найден."));
        Save();
    }

    public void MoveFolder(Guid folderId, Guid? parentId)
    {
        var folder = State.Folders.Single(f => f.Id == folderId);
        RequireFolder(folder.ProjectId, parentId);
        if (parentId == folderId || Descendants(folderId).Contains(parentId ?? Guid.Empty)) throw new ArgumentException(Localization.Text("Нельзя переместить папку внутрь самой себя."));
        if (State.Folders.Any(f => f.Id != folder.Id && f.ProjectId == folder.ProjectId && f.ParentId == parentId && f.Name.Equals(folder.Name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException(Localization.Text("В месте назначения уже есть такая папка."));
        folder.ParentId = parentId; Save();
    }

    public void Link(Guid assetId, Guid projectId, Guid? folderId)
    {
        if (!State.Assets.Any(a => a.Id == assetId)) throw new ArgumentException(Localization.Text("Ассет не найден."));
        RequireProject(projectId); RequireFolder(projectId, folderId);
        var link = State.Links.FirstOrDefault(l => l.AssetId == assetId && l.ProjectId == projectId);
        if (link is null) { link = new WorkspaceLink { AssetId = assetId, ProjectId = projectId }; State.Links.Add(link); }
        link.FolderId = folderId; Save();
    }

    public void Unlink(Guid assetId, Guid projectId) { State.Links.RemoveAll(l => l.AssetId == assetId && l.ProjectId == projectId); Save(); }
    public void RemoveProject(Guid id)
    {
        State.Links.RemoveAll(l => l.ProjectId == id); State.Folders.RemoveAll(f => f.ProjectId == id); State.Projects.RemoveAll(p => p.Id == id); Save();
    }
    public void RemoveFolder(Guid id)
    {
        var folder = State.Folders.Single(f => f.Id == id);
        foreach (var child in State.Folders.Where(f => f.ParentId == id))
        {
            string stem = child.Name; int q = 2;
            while (State.Folders.Any(f => f.Id != child.Id && f.ProjectId == child.ProjectId && f.ParentId == folder.ParentId && f.Name.Equals(child.Name, StringComparison.OrdinalIgnoreCase))) child.Name = stem + " " + q++;
            child.ParentId = folder.ParentId;
        }
        foreach (var link in State.Links.Where(l => l.FolderId == id)) link.FolderId = folder.ParentId;
        State.Folders.Remove(folder); Save();
    }
    public void ForgetAsset(Guid id) { State.Links.RemoveAll(l => l.AssetId == id); State.Assets.RemoveAll(a => a.Id == id); Save(); }

    public string ContentPath(WorkspaceAsset asset)
    {
        string root = Path.Combine(Root, "documents", asset.Id.ToString("N"));
        string path = Path.GetFullPath(Path.Combine(root, asset.ContentFile));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Localization.Text("Недопустимый путь документа."));
        return path;
    }

    public WorkspaceAsset ImportDocument(string path)
    {
        var kind = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ase" or ".aseprite" => WorkspaceAssetKind.Animation, ".tboard" => WorkspaceAssetKind.Board,
            ".ttiles" => WorkspaceAssetKind.Tiles, _ => throw new ArgumentException(Localization.Text("Формат документа не поддерживается."))
        };
        var asset = new WorkspaceAsset { Kind = kind, Name = Path.GetFileName(path), ContentFile = "content" + Path.GetExtension(path).ToLowerInvariant() };
        string target = ContentPath(asset); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(path, target);
        State.Assets.Add(asset); Save(); return asset;
    }

    public WorkspaceAsset CopyForProject(Guid assetId, Guid projectId, ImageLibrary images)
    {
        RequireProject(projectId);
        var link = State.Links.Single(l => l.AssetId == assetId && l.ProjectId == projectId);
        var original = State.Assets.Single(a => a.Id == assetId);
        var copy = JsonSerializer.Deserialize<WorkspaceAsset>(JsonSerializer.Serialize(original))!;
        copy.Id = Guid.NewGuid(); copy.Name = original.Name + Localization.Text(" — копия"); copy.OpenedAt = default; copy.ModifiedAt = DateTimeOffset.Now;
        if (original.Kind == WorkspaceAssetKind.Image)
        {
            var source = images.Load().Single(s => s.Id == original.Id);
            source.SelectedGeneration = source.Generations.FirstOrDefault(g => g.Id == original.SelectedResult) ?? source.SelectedGeneration;
            var cloned = images.Clone(source, copy.Id, copy.Name);
            copy.Thumbnail = cloned.SelectedGeneration?.Thumbnail ?? cloned.Thumbnail;
            copy.SelectedResult = cloned.SelectedGeneration?.Id;
        }
        else
        {
            string target = ContentPath(copy); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(ContentPath(original), target);
            string preview = Path.Combine(Path.GetDirectoryName(ContentPath(original))!, "preview.png");
            if (File.Exists(preview)) File.Copy(preview, Path.Combine(Path.GetDirectoryName(target)!, "preview.png"));
            copy.Thumbnail = original.Thumbnail;
        }
        State.Assets.Add(copy); link.AssetId = copy.Id; Save(); return copy;
    }

    public HashSet<Guid> Descendants(Guid folderId)
    {
        var found = new HashSet<Guid>(); var queue = new Queue<Guid>(); queue.Enqueue(folderId);
        while (queue.Count > 0)
        {
            Guid parent = queue.Dequeue();
            foreach (var folder in State.Folders.Where(f => f.ParentId == parent).ToArray())
                if (found.Add(folder.Id)) queue.Enqueue(folder.Id);
        }
        return found;
    }

    public string Location(WorkspaceLink link)
    {
        var parts = new List<string>(); var visited = new HashSet<Guid>(); Guid? id = link.FolderId;
        while (id is { } value && visited.Add(value))
        {
            var folder = State.Folders.FirstOrDefault(f => f.Id == value); if (folder is null) break;
            parts.Add(folder.Name); id = folder.ParentId;
        }
        parts.Add(State.Projects.First(p => p.Id == link.ProjectId).Name); parts.Reverse(); return string.Join(" / ", parts);
    }

    public IReadOnlyList<WorkspaceAsset> Query(string view, Guid? project, Guid? folder, int scope, string search, int kind, string width, string height, int transparency)
    {
        IEnumerable<WorkspaceAsset> assets = State.Assets;
        if (scope < 2)
        {
            if (view == "unassigned") assets = assets.Where(a => !State.Links.Any(l => l.AssetId == a.Id));
            else if (project is { } projectId)
            {
                var folders = folder is { } folderId ? Descendants(folderId) : [];
                if (folder is { } selected) folders.Add(selected);
                assets = assets.Where(a => State.Links.Any(l => l.AssetId == a.Id && l.ProjectId == projectId &&
                    (scope == 1 || folder is null || l.FolderId is { } id && folders.Contains(id))));
            }
        }
        if (view == "recent" && scope < 2) assets = assets.Where(a => a.OpenedAt != default);
        foreach (string word in search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            assets = assets.Where(a => a.Name.Contains(word, StringComparison.OrdinalIgnoreCase) || a.Tags.Any(t => t.Contains(word.TrimStart('#'), StringComparison.OrdinalIgnoreCase)));
        if (kind > 0) assets = assets.Where(a => (int)a.Kind == kind - 1);
        if (int.TryParse(width, out int w)) assets = assets.Where(a => a.Width == w);
        if (int.TryParse(height, out int h)) assets = assets.Where(a => a.Height == h);
        if (transparency > 0) assets = assets.Where(a => a.Transparent == (transparency == 1));
        foreach (var asset in State.Assets)
            asset.LocationLabel = string.Join(" · ", State.Links.Where(l => l.AssetId == asset.Id && (project is null || scope == 2 || l.ProjectId == project)).Select(Location));
        return (view == "recent" ? assets.OrderByDescending(a => a.OpenedAt) : assets.OrderByDescending(a => a.ModifiedAt)).ToList();
    }

    private void RequireProject(Guid id) { if (!State.Projects.Any(p => p.Id == id)) throw new ArgumentException(Localization.Text("Проект не найден.")); }
    private void RequireFolder(Guid project, Guid? id) { if (id is { } value && !State.Folders.Any(f => f.Id == value && f.ProjectId == project)) throw new ArgumentException(Localization.Text("Папка не принадлежит проекту.")); }
    private static string CleanName(string name) => string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\']) >= 0 ? throw new ArgumentException(Localization.Text("Введите название без разделителей пути.")) : name.Trim();
}
