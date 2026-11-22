using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Text.Json;
using Tessera.Core.Imaging;

namespace Tessera.Desktop;

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
        foreach (var project in _workspaceCatalog!.State.Projects.Where(p => p.IsUnreal))
            _workspaceCatalog.PruneUnrealFolders(project);
        _workspaceBrowser.RebuildTree();
    }

    private WorkspaceProject? UnrealProject(Guid id)
        => _workspaceCatalog?.State.Projects.FirstOrDefault(p => p.Id == id && p.IsUnreal);

    private static UnrealProjectMailbox Mailbox(WorkspaceProject project) => new(project.UnrealProjectPath!);

    private void ConnectUnrealProject(string path)
    {
        var project = _workspaceCatalog!.ConnectUnreal(path);
        ShowWorkspaceBrowser();
        _workspaceBrowser!.Navigate("project", project.Id);
        _workspaceBrowser.RebuildTree();
        PollUnrealProjects();
        if (InstalledUnrealPluginVersion(path) >= UnrealSyncBridge.PluginVersion) return;
        if (TesseraDialogs.Show(this, Localization.Text("InstallOrUpdateTesseraSyncPluginConfirmation"), "Unreal Engine",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            InstallProjectPlugin(path);
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
        UnrealSyncBridge.InstallPlugin(path);
        TesseraDialogs.Show(this, Localization.Text("TesserasyncWasCopied"),
            "Tessera", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void PollUnrealProjects()
    {
        if (!_workspaceReady || WorkspaceBusy || _workspaceCatalog is null || _unrealResolving.Count > 0 || _unrealCreating) return;
        var projects = _workspaceCatalog.State.Projects.Where(p => p.IsUnreal).ToArray();
        if (projects.Length == 0) { _workspaceBrowser!.ShowUnrealStatus(null, false); return; }
        bool changed = false;
        string? status = null;
        bool connected = false;
        WorkspaceAsset[]? visible = null;
        foreach (var project in projects)
        {
            try
            {
                var mailbox = Mailbox(project);
                bool online = mailbox.Online;
                var assets = _workspaceCatalog.State.Assets.Where(a => a.Unreal?.ProjectId == project.Id).ToDictionary(a => a.Id);
                var jobs = mailbox.Jobs();
                if (_workspaceCatalog.State.ProjectId == project.Id)
                {
                    connected = online;
                    int pending = jobs.Count(j => j.Response is null && j.Request.Operation != "read");
                    status = online ? Localization.Text("UnrealConnected") : Localization.Text("UnrealDisconnected");
                    if (pending > 0) status += " · " + Localization.Format("WaitingForUnrealFormat", pending);
                }
                foreach (var job in jobs.Where(j => j.Response is not null && !_unrealResolving.Contains(j.Request.AssetId)))
                {
                    if (!assets.TryGetValue(job.Request.AssetId, out var asset)) continue;
                    if (job.Response!.Status is "saved" or "read" or "moved" or "deleted")
                    {
                        ApplyUnrealResponse(asset, job);
                        if (job.Response.Status is "moved" or "deleted") changed = true;
                    }
                    else if (_unrealReportedJobs.Add(job.Directory) &&
                        (job.Request.Operation != "read" || _unrealOpenReads.Contains(asset.Id)))
                        Notify(job.Request.Operation == "read" ? UnrealJobError(job) :
                            Localization.Format("LabelAndValue", asset.Name, UnrealJobError(job)), NotificationKind.Error);
                }
                long stamp = mailbox.CatalogStamp;
                if (_unrealCatalogStamps.GetValueOrDefault(project.Id) != stamp && mailbox.ReadCatalog() is { } catalog)
                {
                    _workspaceCatalog.MirrorUnreal(project, catalog);
                    _unrealCatalogStamps[project.Id] = stamp;
                    changed = true;
                }
                assets = _workspaceCatalog.State.Assets.Where(a => a.Unreal?.ProjectId == project.Id).ToDictionary(a => a.Id);
                if (!online || jobs.Count(j => j.Response is null && j.Request.Operation == "read") >= 3) continue;
                visible ??= _workspaceBrowser!.VisibleUnrealAssets().Take(8).ToArray();
                var cached = _sources.Select(s => s.Id).ToHashSet();
                var candidate = visible.Concat(assets.Values.Where(a => a.Id == Editor._activeSource?.Id && a.Unreal!.MapKind is null))
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
        _workspaceBrowser!.ShowUnrealStatus(status, connected);
    }

    private void ApplyUnrealResponse(WorkspaceAsset asset, UnrealProjectMailbox.Job job)
    {
        var binding = asset.Unreal!;
        if (!WorkspaceCatalog.UnrealPathComparer.Equals(binding.Path, job.Request.Path))
        {
            job.Remove();
            return;
        }
        if (job.Request.Operation == "delete" && job.Response?.Status == "deleted")
        {
            if (_sources.Any(s => s.Id == asset.Id))
            {
                binding.Missing = binding.NeedsDestination = true;
                _workspaceCatalog!.Save();
            }
            else _workspaceCatalog!.ForgetAsset(asset.Id);
            Notify(Localization.Format("DeletedFromUnreal", asset.Name), NotificationKind.Success);
            job.Remove();
            return;
        }
        if (job.Request.Operation == "move" && job.Response?.Status == "moved")
        {
            if (job.Response.Revision is not { Length: > 0 })
                throw new IOException(Localization.Text("TheUnrealResponseFailedVerification"));
            string movedRevision = job.Response.Revision;
            string destination = job.Request.Destination ?? throw new IOException(Localization.Text("TheUnrealResponseFailedVerification"));
            if (!WorkspaceCatalog.ValidObjectPath(destination)) throw new IOException(Localization.Text("TheUnrealResponseFailedVerification"));
            string previousRevision = binding.Revision;
            binding.Path = destination;
            binding.Revision = movedRevision;
            if (binding.LoadedRevision == previousRevision) binding.LoadedRevision = movedRevision;
            foreach (Guid id in binding.ResultRevisions.Where(pair => pair.Value == previousRevision).Select(pair => pair.Key).ToArray())
                binding.ResultRevisions[id] = movedRevision;
            asset.Name = destination[(destination.LastIndexOf('.') + 1)..];
            if (_sources.FirstOrDefault(s => s.Id == asset.Id) is { } source)
                _library.RenameSource(source, asset.Name + ".png");
            _workspaceCatalog!.Save();
            Notify(Localization.Format("MovedInUnreal", destination), NotificationKind.Success);
            job.Remove();
            return;
        }
        if (!job.HasExport || job.Response?.Revision is not { Length: > 0 } revision)
            throw new IOException(Localization.Text("TheUnrealResponseFailedVerification"));
        bool saved = job.Response.Status == "saved";
        bool materialized = _sources.Any(s => s.Id == asset.Id);
        if (_unrealOpening == asset.Id && _unrealOpenReads.Contains(asset.Id) || materialized && (!saved || binding.MapKind is null))
            LoadUnrealImage(asset, job.Export, revision);
        else
        {
            var image = _library.Resources.Get(job.Export, 180, 128) ?? throw new InvalidDataException(job.Export);
            string preview = UnrealPreviewPath(asset);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            LibraryJson.WriteFile(preview, output => encoder.Save(output));
            _library.Resources.Invalidate(job.Export);
            _library.Resources.Invalidate(preview);
            SetUnrealThumbnail(asset, refresh: true);
            _unrealPreviewRevisions[asset.Id] = revision;
        }
        if (saved)
        {
            if (binding.CreatePending && job.Request.Operation == "create")
                foreach (Guid id in binding.ResultRevisions.Where(pair => pair.Value.Length == 0).Select(pair => pair.Key).ToArray())
                    binding.ResultRevisions[id] = revision;
            binding.CreatePending = false;
            binding.LoadedRevision = revision;
            binding.Revision = revision;
            if (job.Request.ResultId is { } resultId) binding.ResultRevisions[resultId] = revision;
            binding.Missing = false;
            _workspaceCatalog!.Save();
            Notify(Localization.Format("SavedInUnrealFormat", asset.Name), NotificationKind.Success);
        }
        job.Remove();
    }

    private void LoadUnrealImage(WorkspaceAsset asset, string file, string revision)
    {
        var image = PixelEditBuffer.DecodeFile(file);
        var existing = _sources.FirstOrDefault(s => s.Id == asset.Id);
        var source = existing ?? new SourceEntry { Id = asset.Id, DisplayName = asset.Name + ".png" };
        _library.StoreMutableImage(source, image);
        asset.Unreal!.LoadedRevision = revision;
        asset.Width = source.Width;
        asset.Height = source.Height;
        asset.Transparent = HasTransparency(image);
        asset.SetThumbnail(source.PreviewFor(source.SelectedResultId), refresh: true);
        if (existing is null) _sources.Add(source);
        if (ReferenceEquals(Editor._activeSource, source))
        {
            Editor._sourceImage?.Dispose();
            Editor._sourceImage = ImageLibrary.ReadBitmap(_library.SourcePath(source));
            Editor.Preview.UpdateSourcePreview();
        }
        _workspaceCatalog!.Save();
    }

    private string UnrealPreviewPath(WorkspaceAsset asset) => Path.Combine(_library.DirectoryPath, ".unreal-previews", asset.Id.ToString("N") + ".png");

    private void SetUnrealThumbnail(WorkspaceAsset asset, bool refresh = false)
        => asset.SetThumbnail(new ThumbnailResource(_library.Resources, UnrealPreviewPath(asset), 180, 128), refresh);

    private async Task<bool> EnsureUnrealImageAsync(WorkspaceAsset asset)
    {
        if (asset.Unreal is not { } binding || _sources.Any(s => s.Id == asset.Id)) return true;
        var project = UnrealProject(binding.ProjectId);
        if (project is null || binding.Missing) throw new InvalidOperationException(Localization.Text("TheLinkedUnrealAssetIsUnavailable"));
        var mailbox = Mailbox(project);
        if (!mailbox.Online) throw new InvalidOperationException(Localization.Text("OpenProjectUnrealLoadThisTexture"));
        _unrealOpening = asset.Id;
        if (!_unrealOpenReads.Add(asset.Id)) return false;
        try
        {
            var jobs = mailbox.Jobs().Where(j => j.Request.AssetId == asset.Id && j.Request.Operation == "read").ToArray();
            foreach (var job in jobs.Where(j => j.Response is { Status: not "read" })) job.Remove();
            if (!jobs.Any(j => j.Response is null or { Status: "read" }))
                mailbox.Queue(asset.Id, "read", binding.Path, binding.Revision);
            statusLabel.Text = Localization.Text("LoadingTextureFromUnrealAction");
            for (int q = 0; q < 80 && mailbox.Online && IsLoaded; q++)
            {
                await Task.Delay(250);
                if (_unrealOpening != asset.Id) return false;
                if (_sources.Any(s => s.Id == asset.Id)) return true;
                var failed = mailbox.Jobs().FirstOrDefault(j => j.Request.AssetId == asset.Id && j.Request.Operation == "read" && j.Response is { Status: not "read" });
                if (failed is not null) throw new IOException(UnrealJobError(failed));
            }
            Notify(Localization.Text("TheTextureHasNotLoadedYet"), NotificationKind.Warning);
            return false;
        }
        finally { _unrealOpenReads.Remove(asset.Id); }
    }

    private static string UnrealJobError(UnrealProjectMailbox.Job job) => job.Response?.Status switch
    {
        "conflict" => Localization.Text("TheTextureChangedInUnreal"),
        "missing" => Localization.Text("TheLinkedUnrealAssetIsUnavailable"),
        "occupied" => Localization.Text("ThatNameAlreadyExistsInUnreal"),
        _ => job.Response?.Message ?? Localization.Text("SaveIsWaitingForUnreal")
    };

    private void SaveUnrealTexture(WorkspaceAsset asset, string png, Guid? resultId = null)
    {
        var link = asset.Unreal!;
        var project = UnrealProject(link.ProjectId) ?? throw new InvalidOperationException(Localization.Text("ReconnectTheUnrealProject"));
        var mailbox = Mailbox(project);
        if (!_unrealResolving.Add(asset.Id)) return;
        try
        {
            var existing = mailbox.Jobs().Where(j => j.Request.AssetId == asset.Id && j.Request.Operation != "read").ToArray();
            if (existing.Any(j => j.Response is null or { Status: "saved" }))
            {
                Notify(Localization.Text("ThisTextureAlreadyHasAQueued"), NotificationKind.Warning);
                return;
            }
            string revision = resultId is { } id ? link.ResultRevisions.GetValueOrDefault(id, link.LoadedRevision) : link.LoadedRevision;
            var conflict = existing.FirstOrDefault(j => j.Response?.Status == "conflict");
            if (link.CreatePending)
            {
                if (existing.Any(j => j.Response?.Status == "occupied") && !ChooseNewUnrealDestination(asset, project)) return;
                if (ValidateNewUnrealTexture(project, link.Path, asset.Id) is { } error) throw new InvalidOperationException(error);
                revision = "";
            }
            else if (conflict is not null && WorkspaceCatalog.UnrealPathComparer.Equals(conflict.Request.Path, link.Path))
            {
                if (!conflict.HasExport) throw new IOException(Localization.Text("TheUnrealResponseFailedVerification"));
                int choice = _workspaceBrowser!.Choose(Localization.Text("TheTextureChangedInUnreal"), [
                    Localization.Text("LoadTheUnrealVersion"), Localization.Text("ReplaceUnrealVersionSelectedResult")
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
                if (TesseraDialogs.Show(this, UnrealJobError(existing[0]) + "\n" + Localization.Text("RetrySavingConfirmation"),
                    "Unreal Engine", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            }
            string operation = link.CreatePending || link.Missing ? "create" : "save";
            mailbox.Queue(asset.Id, operation, link.Path, revision, operation == "create" ? link.MapKind ?? "color" : link.MapKind, png, resultId);
            foreach (var job in existing) job.Remove();
            statusLabel.Text = Localization.Text("SaveIsWaitingForUnreal");
        }
        finally { _unrealResolving.Remove(asset.Id); }
    }

    private bool ChooseUnrealDestination(WorkspaceAsset asset, WorkspaceProject project, string suggestion)
    {
        var link = asset.Unreal!;
        string? path = _workspaceBrowser!.Prompt(Localization.Text("LinkToAnUnrealTexture"),
            Localization.Text("FullUnrealObjectPath"), suggestion);
        if (path is null) return false;
        path = path.Trim();
        int quote = path.IndexOf('\'');
        if (quote >= 0 && path.EndsWith('\'')) path = path[(quote + 1)..^1];
        if (!WorkspaceCatalog.ValidObjectPath(path)) throw new ArgumentException(Localization.Text("EnterPathLikeGameFolderTexture"));
        var other = _workspaceCatalog!.State.Assets.FirstOrDefault(a => a != asset && a.Unreal?.ProjectId == project.Id &&
            !a.Unreal.Missing && WorkspaceCatalog.UnrealPathComparer.Equals(a.Unreal.Path, path));
        if (other is not null)
        {
            if (_sources.Any(s => s.Id == other.Id) || Mailbox(project).Jobs().Any(j => j.Request.AssetId == other.Id && j.Request.Operation != "read"))
                throw new InvalidOperationException(Localization.Text("ThisAssetAlreadyLinkedAnotherTessera"));
            if (TesseraDialogs.Show(this, Localization.Text("LinkExistingTextureReplaceItWhenConfirmation"), "Unreal Engine",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return false;
            link.Revision = link.LoadedRevision = other.Unreal!.Revision;
            _workspaceCatalog.ForgetAsset(other.Id);
        }
        else if (link.MapKind is null)
            throw new InvalidOperationException(Localization.Text("ChooseExistingTextureUnrealCatalog"));
        else link.Revision = link.LoadedRevision = "";
        link.Path = path;
        link.Missing = other is null;
        link.NeedsDestination = false;
        _workspaceCatalog.Save();
        _unrealCatalogStamps.Remove(project.Id);
        return true;
    }

    private void SaveUnrealMaps(WorkspaceMaterialSession session, MaterialMapDocument document)
    {
        var catalog = _workspaceCatalog!;
        var owner = catalog.State.Assets.FirstOrDefault(a => a.Id == session.SourceId);
        var parent = owner?.Unreal;
        if (parent is null || parent.Missing || UnrealProject(parent.ProjectId) is not { } project) return;
        foreach (var kind in Enum.GetValues<EMaterialMapKind>())
        {
            Guid id = session.MapId(kind);
            var source = _sources.FirstOrDefault(s => s.Id == id);
            bool added = source is null;
            source ??= new SourceEntry { Id = id, DisplayName = owner!.DisplayName + "_" + kind.ToString().ToLowerInvariant() + ".png",
                OriginSourceId = session.SourceId };
            source.OriginGenerationId = session.GenerationId;
            _library.StoreMaterialMap(source, MaterialMapsWindow.ToImage(document.MapPixels(kind), document.Width, document.Height));
            if (added) _sources.Add(source);
            catalog.SyncImages([source]);
            var asset = catalog.State.Assets.Single(a => a.Id == id);
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
