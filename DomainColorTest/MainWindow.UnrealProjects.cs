using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Text.Json;
using PixelArtDownscale;

namespace DomainColorTest;

public partial class MainWindow
{
    private readonly Dictionary<Guid, long> _unrealCatalogStamps = [];
    private readonly Dictionary<Guid, string> _unrealPreviewRevisions = [];
    private readonly HashSet<Guid> _unrealOpenReads = [];
    private readonly HashSet<string> _unrealReportedJobs = [];
    private readonly HashSet<Guid> _unrealResolving = [];
    private Guid? _unrealOpening;

    private void StartUnrealProjects()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (_, _) => PollUnrealProjects();
        timer.Start();
        Closed += (_, _) => timer.Stop();
        _workspaceBrowser!.ConnectUnrealRequested += async () =>
        {
            try { await SelectUnrealProjectAsync(); }
            catch (Exception error) { WorkspaceError(error); }
        };
        if (_unrealSync.ProjectPath is { } previous && File.Exists(previous))
            _workspaceCatalog!.ConnectUnreal(previous);
        _workspaceBrowser.RebuildTree();
    }

    private WorkspaceProject? UnrealProject(Guid id)
        => _workspaceCatalog?.State.Projects.FirstOrDefault(p => p.Id == id && p.IsUnreal);

    private static UnrealProjectMailbox Mailbox(WorkspaceProject project) => new(project.UnrealProjectPath!);

    private async Task ConnectUnrealProjectAsync(string path)
    {
        var project = _workspaceCatalog!.ConnectUnreal(path);
        ShowWorkspaceBrowser();
        _workspaceBrowser!.Navigate("project", project.Id);
        _workspaceBrowser.RebuildTree();
        PollUnrealProjects();
        if (!_workspaceCatalog.State.Folders.Any(f => f.ProjectId == project.Id))
        {
            string[] folders = await Task.Run(() => ReadUnrealFolders(path));
            if (!_workspaceCatalog.State.Folders.Any(f => f.ProjectId == project.Id))
            {
                _workspaceCatalog.MirrorUnreal(project, new UnrealProjectMailbox.Catalog(2, folders, [], []));
                _workspaceBrowser.RebuildTree();
            }
        }
        if (InstalledUnrealPluginVersion(path) >= 2) return;
        if (TesseraDialog.Show(this, Localization.Text("Установить или обновить плагин Tessera Sync для этого проекта?"), "Unreal Engine", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            InstallProjectPlugin(path);
    }

    private static string[] ReadUnrealFolders(string projectPath)
    {
        var folders = new List<string> { "/Game" };
        string content = Path.Combine(Path.GetDirectoryName(projectPath)!, "Content");
        if (!Directory.Exists(content)) return folders.ToArray();
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var pending = new Queue<(string Directory, string Path)>();
        pending.Enqueue((content, "/Game"));
        while (pending.TryDequeue(out var entry))
        {
            foreach (string directory in Directory.EnumerateDirectories(entry.Directory, "*", options))
            {
                string path = entry.Path + "/" + Path.GetFileName(directory);
                if (!WorkspaceCatalog.VisibleUnrealFolder(path)) continue;
                folders.Add(path);
                pending.Enqueue((directory, path));
            }
        }
        return folders.ToArray();
    }

    private static int InstalledUnrealPluginVersion(string projectPath)
    {
        string descriptor = Path.Combine(Path.GetDirectoryName(projectPath)!, "Plugins", "TesseraSync", "TesseraSync.uplugin");
        try
        {
            using var data = JsonDocument.Parse(File.ReadAllText(descriptor));
            return data.RootElement.GetProperty("Version").GetInt32();
        }
        catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { return 0; }
    }

    private void InstallProjectPlugin(string path)
    {
        _unrealSync.SelectProject(path);
        _unrealSync.InstallPlugin();
        TesseraDialog.Show(this, Localization.Text("Плагин TesseraSync скопирован. В Unreal включите его и перезапустите редактор. Плагины Python Editor Script и Editor Scripting Utilities должны быть включены."),
            "Tessera", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void PollUnrealProjects()
    {
        if (!_workspaceReady || WorkspaceBusy || _workspaceCatalog is null || _unrealResolving.Count > 0) return;
        var projects = _workspaceCatalog.State.Projects.Where(p => p.IsUnreal).ToArray();
        if (projects.Length == 0) return;
        bool changed = false;
        string? status = null;
        WorkspaceAsset[]? visible = null;
        foreach (var project in projects)
        {
            try
            {
                var mailbox = Mailbox(project);
                bool online = mailbox.Online;
                long stamp = mailbox.CatalogStamp;
                if (_unrealCatalogStamps.GetValueOrDefault(project.Id) != stamp && mailbox.ReadCatalog() is { } catalog)
                {
                    _workspaceCatalog.MirrorUnreal(project, catalog);
                    _unrealCatalogStamps[project.Id] = stamp;
                    changed = true;
                }
                var assets = _workspaceCatalog.State.Assets.Where(a => a.Unreal?.ProjectId == project.Id).ToDictionary(a => a.Id);
                var jobs = mailbox.Jobs();
                if (_workspaceCatalog.State.ProjectId == project.Id)
                {
                    int pending = jobs.Count(j => j.Response is null && j.Request.Operation != "read");
                    status = online ? Localization.Text("Unreal подключён") : Localization.Text("Unreal не подключён");
                    if (pending > 0) status += " · " + Localization.Format("Ожидает Unreal: {0}", pending);
                }
                foreach (var job in jobs.Where(j => j.Response is not null && !_unrealResolving.Contains(j.Request.AssetId)))
                {
                    if (!assets.TryGetValue(job.Request.AssetId, out var asset)) continue;
                    if (job.Response!.Status is "saved" or "read") ApplyUnrealResponse(asset, job);
                    else if (_unrealReportedJobs.Add(job.Directory) &&
                        (job.Request.Operation != "read" || _unrealOpenReads.Contains(asset.Id)))
                        statusLabel.Text = job.Request.Operation == "read" ? UnrealJobError(job) :
                            Localization.Format("{0}: {1} · Ctrl+S", asset.Name, UnrealJobError(job));
                }
                if (!online || jobs.Count(j => j.Response is null && j.Request.Operation == "read") >= 3) continue;
                visible ??= _workspaceBrowser!.VisibleUnrealAssets().Take(8).ToArray();
                var cached = _sources.Select(s => s.Id).ToHashSet();
                var candidate = visible.Concat(assets.Values.Where(a => cached.Contains(a.Id) && a.Unreal!.MapKind is null))
                    .FirstOrDefault(a => a.Unreal is { Missing: false } link && link.ProjectId == project.Id &&
                        (cached.Contains(a.Id) ? link.MapKind is null && link.LoadedRevision != link.Revision :
                            _unrealPreviewRevisions.GetValueOrDefault(a.Id) != link.Revision) &&
                        !jobs.Any(j => j.Request.AssetId == a.Id));
                if (candidate?.Unreal is { } binding)
                    mailbox.Queue(candidate.Id, "read", binding.Path, binding.Revision);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                if (_workspaceCatalog.State.ProjectId == project.Id) status = error.Message;
            }
        }
        if (changed)
        {
            _workspaceBrowser!.RebuildTree();
            if (_workspaceBrowser.IsVisible) _workspaceBrowser.Refresh();
            RefreshWorkspaceSources();
        }
        _workspaceBrowser!.ShowUnrealStatus(status);
    }

    private void ApplyUnrealResponse(WorkspaceAsset asset, UnrealProjectMailbox.Job job)
    {
        var binding = asset.Unreal!;
        if (!WorkspaceCatalog.UnrealPathComparer.Equals(binding.Path, job.Request.Path))
        {
            job.Remove();
            return;
        }
        if (!job.HasExport || job.Response?.Revision is not { Length: > 0 } revision)
            throw new IOException(Localization.Text("Ответ Unreal не прошёл проверку."));
        bool saved = job.Response.Status == "saved";
        bool materialized = _sources.Any(s => s.Id == asset.Id);
        if (_unrealOpenReads.Contains(asset.Id) || materialized && (!saved || binding.MapKind is null))
            LoadUnrealImage(asset, job.Export, revision);
        else
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 180;
            using var stream = File.OpenRead(job.Export);
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            asset.Thumbnail = image;
            _unrealPreviewRevisions[asset.Id] = revision;
        }
        if (saved)
        {
            binding.LoadedRevision = revision;
            if (job.Request.ResultId is { } resultId) binding.ResultRevisions[resultId] = revision;
            binding.Missing = false;
            _workspaceCatalog!.Save();
            statusLabel.Text = Localization.Format("Сохранено в Unreal: {0}", asset.Name);
        }
        job.Remove();
    }

    private void LoadUnrealImage(WorkspaceAsset asset, string file, string revision)
    {
        var image = TileAsset.DecodeFile(file);
        var existing = _sources.FirstOrDefault(s => s.Id == asset.Id);
        var source = existing ?? new SourceEntry { Id = asset.Id, DisplayName = asset.Name + ".png" };
        _library.StoreMutableImage(source, image);
        asset.Unreal!.LoadedRevision = revision;
        asset.Width = source.Width;
        asset.Height = source.Height;
        asset.Transparent = HasTransparency(image);
        asset.Thumbnail = source.SelectedGeneration?.Thumbnail ?? source.Thumbnail;
        if (existing is null) _sources.Add(source);
        if (ReferenceEquals(_activeSource, source))
        {
            _sourceImage?.Dispose();
            _sourceImage = ImageLibrary.ReadBitmap(_library.SourcePath(source));
            UpdateSourcePreview();
        }
        _workspaceCatalog!.Save();
    }

    private async Task<bool> EnsureUnrealImageAsync(WorkspaceAsset asset)
    {
        if (asset.Unreal is not { } binding || _sources.Any(s => s.Id == asset.Id)) return true;
        var project = UnrealProject(binding.ProjectId);
        if (project is null || binding.Missing) throw new InvalidOperationException(Localization.Text("Связанный ассет Unreal недоступен."));
        var mailbox = Mailbox(project);
        if (!mailbox.Online) throw new InvalidOperationException(Localization.Text("Откройте проект в Unreal для загрузки текстуры."));
        _unrealOpening = asset.Id;
        if (!_unrealOpenReads.Add(asset.Id)) return false;
        try
        {
            var jobs = mailbox.Jobs().Where(j => j.Request.AssetId == asset.Id && j.Request.Operation == "read").ToArray();
            foreach (var job in jobs.Where(j => j.Response is { Status: not "read" })) job.Remove();
            if (!jobs.Any(j => j.Response is null or { Status: "read" }))
                mailbox.Queue(asset.Id, "read", binding.Path, binding.Revision);
            statusLabel.Text = Localization.Text("Загрузка текстуры из Unreal…");
            for (int q = 0; q < 80 && mailbox.Online && IsLoaded; q++)
            {
                await Task.Delay(250);
                if (_unrealOpening != asset.Id) return false;
                if (_sources.Any(s => s.Id == asset.Id)) return true;
                var failed = mailbox.Jobs().FirstOrDefault(j => j.Request.AssetId == asset.Id && j.Request.Operation == "read" && j.Response is { Status: not "read" });
                if (failed is not null) throw new IOException(UnrealJobError(failed));
            }
            statusLabel.Text = Localization.Text("Текстура ещё не загружена. Проверьте состояние Unreal и откройте её снова.");
            return false;
        }
        finally { _unrealOpenReads.Remove(asset.Id); }
    }

    private static string UnrealJobError(UnrealProjectMailbox.Job job) => job.Response?.Status switch
    {
        "conflict" => Localization.Text("Текстура изменилась в Unreal."),
        "missing" => Localization.Text("Связанный ассет Unreal недоступен."),
        "occupied" => Localization.Text("Это имя уже занято в Unreal."),
        _ => job.Response?.Message ?? Localization.Text("Запись ожидает Unreal.")
    };

    private void SaveUnrealTexture(WorkspaceAsset asset, string png, Guid? resultId = null)
    {
        var link = asset.Unreal!;
        var project = UnrealProject(link.ProjectId) ?? throw new InvalidOperationException(Localization.Text("Подключите проект Unreal снова."));
        var mailbox = Mailbox(project);
        if (!_unrealResolving.Add(asset.Id)) return;
        try
        {
            var existing = mailbox.Jobs().Where(j => j.Request.AssetId == asset.Id && j.Request.Operation != "read").ToArray();
            if (existing.Any(j => j.Response is null or { Status: "saved" }))
            {
                statusLabel.Text = Localization.Text("Для этой текстуры уже есть запись в очереди. Повторите Ctrl+S после её обработки.");
                return;
            }
            string revision = resultId is { } id ? link.ResultRevisions.GetValueOrDefault(id, link.LoadedRevision) : link.LoadedRevision;
            var conflict = existing.FirstOrDefault(j => j.Response?.Status == "conflict");
            if (conflict is not null && WorkspaceCatalog.UnrealPathComparer.Equals(conflict.Request.Path, link.Path))
            {
                if (!conflict.HasExport) throw new IOException(Localization.Text("Ответ Unreal не прошёл проверку."));
                int choice = _workspaceBrowser!.Choose(Localization.Text("Текстура изменилась в Unreal."), [
                    Localization.Text("Загрузить версию Unreal"), Localization.Text("Заменить версию Unreal выбранным результатом")
                ]);
                if (choice < 0) return;
                revision = conflict.Response!.Revision!;
                if (choice == 0)
                {
                    LoadUnrealImage(asset, conflict.Export, revision);
                    foreach (var job in existing) job.Remove();
                    return;
                }
            }
            else if (link.Missing && (link.MapKind is null || existing.Length > 0 || link.LoadedRevision.Length > 0) ||
                existing.Any(j => j.Response?.Status == "missing" && WorkspaceCatalog.UnrealPathComparer.Equals(j.Request.Path, link.Path)))
            {
                if (!ChooseUnrealDestination(asset, project, link.Path)) return;
                revision = link.LoadedRevision;
            }
            else if (existing.Any(j => j.Response?.Status is "error"))
            {
                if (TesseraDialog.Show(this, UnrealJobError(existing[0]) + "\n" + Localization.Text("Повторить сохранение?"),
                    "Unreal Engine", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            }
            string operation = link.Missing ? "create" : "save";
            mailbox.Queue(asset.Id, operation, link.Path, revision, link.MapKind, png, resultId);
            foreach (var job in existing) job.Remove();
            statusLabel.Text = Localization.Text("Запись ожидает Unreal.");
        }
        finally { _unrealResolving.Remove(asset.Id); }
    }

    private bool ChooseUnrealDestination(WorkspaceAsset asset, WorkspaceProject project, string suggestion)
    {
        var link = asset.Unreal!;
        string? path = _workspaceBrowser!.Prompt(Localization.Text("Связать с текстурой Unreal"),
            Localization.Text("Полный путь к объекту Unreal"), suggestion);
        if (path is null) return false;
        path = path.Trim();
        int quote = path.IndexOf('\'');
        if (quote >= 0 && path.EndsWith('\'')) path = path[(quote + 1)..^1];
        if (!WorkspaceCatalog.ValidObjectPath(path)) throw new ArgumentException(Localization.Text("Введите путь вида /Game/Папка/Текстура.Текстура."));
        var other = _workspaceCatalog!.State.Assets.FirstOrDefault(a => a != asset && a.Unreal?.ProjectId == project.Id &&
            !a.Unreal.Missing && WorkspaceCatalog.UnrealPathComparer.Equals(a.Unreal.Path, path));
        if (other is not null)
        {
            if (_sources.Any(s => s.Id == other.Id) || Mailbox(project).Jobs().Any(j => j.Request.AssetId == other.Id && j.Request.Operation != "read"))
                throw new InvalidOperationException(Localization.Text("Этот ассет уже связан с другой текстурой Tessera."));
            if (TesseraDialog.Show(this, Localization.Text("Связать с существующей текстурой и заменить её при сохранении?"), "Unreal Engine", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return false;
            link.Revision = link.LoadedRevision = other.Unreal!.Revision;
            _workspaceCatalog.ForgetAsset(other.Id);
        }
        else if (link.MapKind is null)
            throw new InvalidOperationException(Localization.Text("Выберите существующую текстуру из каталога Unreal."));
        else link.Revision = link.LoadedRevision = "";
        link.Path = path;
        link.Missing = other is null;
        link.NeedsDestination = false;
        _workspaceCatalog.Save();
        _unrealCatalogStamps.Remove(project.Id);
        return true;
    }

    private void SaveUnrealMaps(WorkspaceMaterialSession session)
    {
        var parent = _workspaceCatalog!.State.Assets.FirstOrDefault(a => a.Id == session.SourceId)?.Unreal;
        if (parent is null || parent.Missing || UnrealProject(parent.ProjectId) is not { } project) return;
        foreach (var kind in Enum.GetValues<MaterialMapKind>())
        {
            Guid id = session.MapId(kind);
            var asset = _workspaceCatalog.State.Assets.Single(a => a.Id == id);
            var source = _sources.Single(s => s.Id == id);
            if (asset.Unreal is null)
            {
                string suffix = "_" + kind.ToString().ToLowerInvariant();
                string package = parent.Path[..parent.Path.LastIndexOf('.')] + suffix;
                string suggested = package + "." + package[(package.LastIndexOf('/') + 1)..];
                asset.Unreal = new UnrealTextureLink { ProjectId = project.Id, MapKind = suffix[1..], Missing = true };
                _unrealResolving.Add(asset.Id);
                try
                {
                    if (!ChooseUnrealDestination(asset, project, suggested))
                    {
                        asset.Unreal = null;
                        continue;
                    }
                }
                catch { asset.Unreal = null; throw; }
                finally { _unrealResolving.Remove(asset.Id); }
            }
            SaveUnrealTexture(asset, _library.SourcePath(source));
        }
    }
}
