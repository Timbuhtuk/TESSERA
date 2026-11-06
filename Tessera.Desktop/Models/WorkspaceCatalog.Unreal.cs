using System.IO;

namespace Tessera.Desktop;

public sealed partial class WorkspaceCatalog
{
    internal WorkspaceProject ConnectUnreal(string path)
    {
        string full = Path.GetFullPath(path);
        if (!File.Exists(full) || !Path.GetExtension(full).Equals(".uproject", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(Localization.Text("Выберите существующий файл .uproject."));
        var existing = State.Projects.FirstOrDefault(p => p.IsUnreal && UnrealPathComparer.Equals(p.UnrealProjectPath, full));
        if (existing is not null) return existing;
        var project = new WorkspaceProject
        {
            Name = Path.GetFileNameWithoutExtension(full),
            Kind = EWorkspaceProjectKind.Unreal,
            UnrealProjectPath = full
        };
        State.Projects.Add(project);
        Save();
        return project;
    }

    internal bool IsUnrealProject(Guid? id) => State.Projects.Any(p => p.Id == id && p.IsUnreal);
    internal static readonly StringComparer UnrealPathComparer = StringComparer.OrdinalIgnoreCase;

    internal void MirrorUnreal(WorkspaceProject project, UnrealProjectMailbox.Catalog catalog)
    {
        var previousFolders = State.Folders.Where(f => f.ProjectId == project.Id && f.UnrealPath is not null)
            .ToDictionary(f => f.UnrealPath!, UnrealPathComparer);
        var folders = new Dictionary<string, WorkspaceFolder>(UnrealPathComparer);
        WorkspaceFolder Folder(string path)
        {
            if (folders.TryGetValue(path, out var found)) return found;
            int separator = path.LastIndexOf('/');
            var folder = previousFolders.GetValueOrDefault(path) ?? new WorkspaceFolder { ProjectId = project.Id, UnrealPath = path };
            folder.Name = path == "/Game" ? "Content" : path[(separator + 1)..];
            folder.ParentId = path == "/Game" ? null : Folder(path[..separator]).Id;
            folders.Add(path, folder);
            return folder;
        }

        var entries = catalog.Textures.Where(t => ValidObjectPath(t.Path))
            .GroupBy(t => t.Path, UnrealPathComparer).ToDictionary(g => g.Key, g => g.First(), UnrealPathComparer);
        var links = new List<WorkspaceLink>();
        var linked = State.Assets.Where(a => a.Unreal?.ProjectId == project.Id).ToArray();
        var claimed = new HashSet<string>(UnrealPathComparer);
        void Place(WorkspaceAsset asset, UnrealProjectMailbox.Texture entry)
        {
            var binding = asset.Unreal!;
            binding.Path = entry.Path;
            binding.Revision = entry.Revision;
            binding.Missing = false;
            binding.NeedsDestination = false;
            int dot = entry.Path.LastIndexOf('.');
            asset.Name = entry.Path[(dot + 1)..];
            var folder = Folder(entry.Path[..entry.Path.LastIndexOf('/')]);
            links.Add(new WorkspaceLink { AssetId = asset.Id, ProjectId = project.Id, FolderId = folder.Id });
            claimed.Add(entry.Path);
        }

        foreach (var asset in linked.Where(a => a.Unreal!.CreatePending))
        {
            var binding = asset.Unreal!;
            if (!ValidObjectPath(binding.Path)) continue;
            asset.Name = binding.Path[(binding.Path.LastIndexOf('.') + 1)..];
            var folder = Folder(binding.Path[..binding.Path.LastIndexOf('/')]);
            links.Add(new WorkspaceLink { AssetId = asset.Id, ProjectId = project.Id, FolderId = folder.Id });
            claimed.Add(binding.Path);
        }
        foreach (var asset in linked.Where(a => !a.Unreal!.CreatePending))
        {
            var binding = asset.Unreal!;
            if (!binding.NeedsDestination && entries.TryGetValue(binding.Path, out var entry) && !claimed.Contains(entry.Path))
                Place(asset, entry);
            else binding.Missing = true;
        }
        foreach (var asset in linked.Where(a => a.Unreal!.Missing && !a.Unreal.CreatePending))
        {
            var binding = asset.Unreal!;
            if (catalog.Redirects.TryGetValue(binding.Path, out string? target) &&
                entries.TryGetValue(target, out var entry) && !claimed.Contains(entry.Path))
                Place(asset, entry);
            else if (binding.Revision.Length > 0)
                binding.NeedsDestination = true;
        }
        foreach (var entry in entries.Values.Where(t => !claimed.Contains(t.Path)))
        {
            var asset = new WorkspaceAsset { Unreal = new UnrealTextureLink { ProjectId = project.Id } };
            State.Assets.Add(asset);
            Place(asset, entry);
        }
        State.Links.RemoveAll(l => l.ProjectId == project.Id);
        State.Links.AddRange(links);
        State.Folders.RemoveAll(f => f.ProjectId == project.Id);
        State.Folders.AddRange(folders.Values);
        if (State.ProjectId == project.Id && State.FolderId is { } selected && !folders.Values.Any(f => f.Id == selected))
            State.FolderId = null;
        Save();
    }

    internal void PruneUnrealFolders(WorkspaceProject project)
    {
        var folders = State.Folders.Where(f => f.ProjectId == project.Id).ToDictionary(f => f.Id);
        var textures = State.Assets.Where(a => a.Unreal?.ProjectId == project.Id && (!a.Unreal.Missing || a.Unreal.CreatePending)).Select(a => a.Id).ToHashSet();
        var used = new HashSet<Guid>();
        foreach (var link in State.Links.Where(l => l.ProjectId == project.Id && textures.Contains(l.AssetId)))
        {
            Guid? parent = link.FolderId;
            while (parent is { } id && folders.TryGetValue(id, out var folder) && used.Add(id))
                parent = folder.ParentId;
        }
        if (State.Folders.RemoveAll(f => f.ProjectId == project.Id && !used.Contains(f.Id)) == 0) return;
        if (State.ProjectId == project.Id && State.FolderId is { } selected && !used.Contains(selected))
            State.FolderId = null;
        Save();
    }

    internal static bool ValidObjectPath(string path)
    {
        int dot = path.LastIndexOf('.');
        int slash = path.LastIndexOf('/');
        return slash >= 5 && dot > slash + 1 && path[(slash + 1)..dot] == path[(dot + 1)..] &&
            VisibleUnrealFolder(path[..dot]);
    }

    internal static bool VisibleUnrealFolder(string path)
    {
        if (path == "/Game") return true;
        if (!path.StartsWith("/Game/", StringComparison.Ordinal)) return false;
        return path[6..].Split('/').All(part => part.Length > 0 && part.IndexOfAny(['\\', '.', ':', '*', '?', '"', '<', '>', '|', '\r', '\n']) < 0 &&
            !part.Equals("__ExternalActors__", StringComparison.OrdinalIgnoreCase) && !part.Equals("__ExternalObjects__", StringComparison.OrdinalIgnoreCase));
    }
}
