using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

public partial class MainWindow
{
    internal WorkspaceCatalog? _workspaceCatalog;

    private WorkspaceBrowser? _workspaceBrowser;

    internal WorkspaceAsset? _workspaceActive;

    private readonly HashSet<Guid> _observedSources = [];

    internal readonly ObservableCollection<SourceEntry> _workspaceSources = [];

    internal Guid? _workspaceSourceProject;

    internal bool _workspaceSourcesUnassigned;

    internal bool _workspaceReady, _workspaceSyncing, _workspaceImporting;

    public WorkspaceCatalog? Workspace => _workspaceCatalog;

    public WorkspaceBrowser? Browser => _workspaceBrowser;

    public void EnableWorkspacePrototype(bool enableUpdates = false)
    {
        WorkspaceBrushSafety.Register();
        _autoCheckUpdates = enableUpdates;
        _workspaceCatalog = new WorkspaceCatalog(_library.DirectoryPath);
        Editor.ReleaseImageEditor();
        LibraryPage.libraryCards.ItemsSource = null;
        var assetsById = _workspaceCatalog.State.Assets.ToDictionary(asset => asset.Id);
        foreach (var asset in assetsById.Values.Where(asset => asset.Unreal is not null)) SetUnrealThumbnail(asset);
        foreach (var source in _sources)
            if (assetsById.TryGetValue(source.Id, out var saved))
            {
                source.RestoreSelection(saved.SelectedResult);
                source.DraftSettings = saved.Draft;
            }
        _workspaceCatalog.SyncImages(_sources);
        Title = enableUpdates ? "TESSERA" : "TESSERA · Workspace prototype";
        _workspaceBrowser = LibraryPage.browser;
        _workspaceBrowser.ImportRequested += async paths => await ImportWorkspaceFilesAsync(paths);
        _workspaceBrowser.OpenRequested += async asset => await OpenWorkspaceAssetAsync(asset);
        _workspaceBrowser.ActionRequested += async (action, asset) =>
        {
            try { await WorkspaceActionAsync(action, asset); }
            catch (Exception ex) { WorkspaceError(ex); }
        };
        _workspaceBrowser.StatusChanged += Notify;
        _workspaceBrowser.ScopeChanging = ChangeWorkspaceScope;
        _workspaceBrowser.ScopeChanged = () =>
        {
            if (_workspaceActive is not { Kind: EWorkspaceAssetKind.Image } asset || Editor._activeSource is null) return;
            Editor.History.ResetSourceFolder();
            SetWorkspaceSourceScope(asset);
        };
        asepriteWorkspace.WorkspaceImport = async paths =>
        {
            PersistWorkspaceState();
            await ImportWorkspaceFilesAsync(paths);
            _workspaceActive = null;
            await asepriteWorkspace.AddFilesAsync(paths);
        };
        _workspaceBrowser.Resources.MergedDictionaries.Add(Resources);
        _workspaceCatalog.ResolveTransparency = ResolveWorkspaceTransparency;
        _workspaceBrowser.Bind(_workspaceCatalog);
        Editor.colorToolGroup.Header = Localization.Text("Palette");
        Editor.colorOnlyButton.Content = Localization.Text("CreateResult");
        Editor.resultPreview.EmptyText = Localization.Text("ChooseAToolFromTheMenu");
        updateButton.Content = enableUpdates ? UpdateButtonText() : Localization.Text("WorkspacePrototype");
        updateButton.IsEnabled = checkUpdatesMenuItem.IsEnabled = enableUpdates;
        Closing += (_, e) =>
        {
            if (e.Cancel) return;
            try { PersistWorkspaceState(); } catch (Exception ex) { e.Cancel = true; WorkspaceError(ex); }
        };
        _sources.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
                foreach (SourceEntry source in e.OldItems)
                {
                    source.GenerationsChanged -= WorkspaceHistoryChanged;
                    _observedSources.Remove(source.Id);
                    _workspaceCatalog.ForgetAsset(source.Id);
                }
            SynchronizeWorkspace();
        };
        moodboardWorkspace.ProjectSaved += path =>
        {
            if (_workspaceActive is not { Kind: EWorkspaceAssetKind.Board } asset || _workspaceCatalog.ContentPath(asset) != path) return;
            asset.ModifiedAt = DateTimeOffset.Now;
            UpdateDocumentPreview(asset); _workspaceCatalog.Save();
        };
        foreach (var document in _workspaceCatalog.State.Assets.Where(a => a.Kind != EWorkspaceAssetKind.Image)) SetDocumentThumbnail(document);
        Editor.History.ConfigureSourceTray();
        _workspaceReady = true; SynchronizeWorkspace(); StartUnrealProjects(); ShowHome();
    }

    internal bool WorkspaceBusy => Editor._processing || _workspaceImporting || iconWorkspace.IsBusy || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy || moodboardWorkspace.IsBusy;

    internal void WorkspaceError(Exception ex)
    {
        Notify(ex.Message, NotificationKind.Error);
        TesseraDialogs.Show(this, ex.Message, "Tessera", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public async Task ImportWorkspaceFilesAsync(IReadOnlyList<string> paths)
    {
        if (_workspaceCatalog is null || WorkspaceBusy) return;
        string[] progressFiles = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (progressFiles.Length == 0) return;
        using var operation = BeginOperationProgress(this, Localization.Text("AddingImagesAction"));
        int progressDone = 0;
        _workspaceImporting = true;
        _workspaceBrowser!.IsEnabled = false;
        UpdateBusyIndicator();
        try
        {
            var failures = new List<string>(); var imported = new List<WorkspaceAsset>();
            foreach (string path in progressFiles)
            {
                WorkspaceAsset? pendingDocument = null;
                try
                {
                    WorkspaceAsset asset;
                    if (Path.GetExtension(path).ToLowerInvariant() is ".ase" or ".aseprite" or ".tboard")
                    {
                        asset = _workspaceCatalog.ImportDocument(path);
                        pendingDocument = asset;
                        if (asset.Kind == EWorkspaceAssetKind.Animation)
                        {
                            string previewFolder = Path.Combine(Path.GetDirectoryName(_workspaceCatalog.ContentPath(asset))!, "preview-build");
                            var converted = await Task.Run(() => Tessera.Aseprite.AsepriteConverter.Convert(_workspaceCatalog.ContentPath(asset), previewFolder, new Tessera.Aseprite.AsepriteExportOptions(), progress: operation.Work.Slice(progressDone, progressFiles.Length)));
                            File.Copy(converted.ImagePath, Path.Combine(Path.GetDirectoryName(_workspaceCatalog.ContentPath(asset))!, "preview.png"), true);
                            asset.Width = converted.Width; asset.Height = converted.Height;
                        }
                        UpdateDocumentPreview(asset);
                    }
                    else
                    {
                        var existingSources = _sources.ToArray();
                        var source = await Task.Run(() => _library.Import(path, existingSources));
                        if (!_sources.Contains(source)) _sources.Add(source);
                        _workspaceCatalog.SyncImages(_sources); asset = _workspaceCatalog.State.Assets.Single(a => a.Id == source.Id);
                        UpdateImageMetadata(asset, source);
                    }
                    if (_workspaceCatalog.State.ProjectId is { } project && !_workspaceCatalog.IsUnrealProject(project)) _workspaceCatalog.Link(asset.Id, project, _workspaceCatalog.State.FolderId);
                    imported.Add(asset);
                }
                catch (Exception ex) { if (pendingDocument is not null) _workspaceCatalog.ForgetAsset(pendingDocument.Id); failures.Add(Path.GetFileName(path) + ": " + ex.Message); }
                operation.Work.Report((double)++progressDone / progressFiles.Length);
            }
            RefreshWorkspaceSources();
            _workspaceCatalog.Save(); _workspaceBrowser!.Refresh(imported.LastOrDefault()?.Id);
            if (failures.Count == 0) operation.Complete();
            string summary = Localization.Format("AddedFormat", imported.Count) + (failures.Count > 0
                ? Localization.Text("ErrorsPrefix") + string.Join("; ", failures)
                : Localization.Text("SelectAMaterialToStart"));
            Notify(summary, BatchNotificationKind(imported.Count, failures.Count));
        }
        finally { _workspaceImporting = false; _workspaceBrowser.IsEnabled = true; UpdateBusyIndicator(); }
    }

    private async Task PromptWorkspaceFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = Localization.Text("AddMaterials"),
            Multiselect = true,
            Filter = Localization.Text("TesseraMaterialsFileFilterLabel")
        };
        if (dialog.ShowDialog(this) == true) await ImportWorkspaceFilesAsync(dialog.FileNames);
    }

    public async Task OpenWorkspaceAssetAsync(WorkspaceAsset asset)
    {
        if (_workspaceCatalog is null || WorkspaceBusy || !Enum.IsDefined(asset.Kind)) return;
        bool sameAsset = _workspaceActive?.Id == asset.Id;
        if (!sameAsset && !moodboardWorkspace.CanLeave()) return;
        try
        {
            _unrealOpening = asset.Id;
            if (!await EnsureUnrealImageAsync(asset)) return;
            PersistWorkspaceState();
            if (!sameAsset) ReleaseWorkspaceAsset();
            _workspaceActive = asset; asset.OpenedAt = DateTimeOffset.Now;
            if (asset.Kind == EWorkspaceAssetKind.Image)
            {
                var source = _sources.Single(s => s.Id == asset.Id);
                SetWorkspaceSourceScope(asset);
                Editor.SelectSource(source);
                if (sameAsset && source.SelectedGeneration is { } selected) Editor.SelectGeneration(selected);
                if (sameAsset && asset.Draft is { } draft) Editor.Tools.RestoreSettings(draft);
                Editor.ProcessingBasis = asset.Basis == 1 && Editor._downscaledResult is not null ? 1 : 0;
                Editor.Preview.PreviewZoom = Math.Clamp(asset.Zoom, 0, EditorPreview.ZoomFactors.Length - 1);
                Editor.Preview.PreviewBackground = Math.Clamp(asset.Background, 0, 3);
                Editor.Dock.OpenDefaultSizeTool();
            }
            else if (asset.Kind == EWorkspaceAssetKind.Animation)
            {
                ShowAsepriteWorkspace(); asepriteWorkspace.Clear(); asepriteWorkspace.ApplyWorkspaceSettings(asset);
                await asepriteWorkspace.AddFilesAsync([_workspaceCatalog.ContentPath(asset)]);
                PersistWorkspaceState();
            }
            else if (asset.Kind == EWorkspaceAssetKind.Board)
            {
                if (sameAsset && moodboardWorkspace.HasBoard) { ShowMoodboardWorkspace(); return; }
                ShowMoodboardWorkspace(); await moodboardWorkspace.LoadFromFileAsync(_workspaceCatalog.ContentPath(asset));
                moodboardWorkspace.WorkspaceSavePath = _workspaceCatalog.ContentPath(asset);
                if (!File.Exists(Path.Combine(Path.GetDirectoryName(moodboardWorkspace.WorkspaceSavePath)!, "preview.png")))
                    UpdateDocumentPreview(asset);
            }
            _workspaceCatalog.State.SelectedAsset = asset.Id; _workspaceCatalog.Save(); UpdateNavigationState();
        }
        catch (Exception ex) { WorkspaceError(ex); }
    }

    private async Task WorkspaceActionAsync(string action, WorkspaceAsset? asset)
    {
        if (_workspaceCatalog is null || WorkspaceBusy) return;
        if (action == "import")
        {
            await PromptWorkspaceFilesAsync();
            return;
        }
        if (action == "material-maps" && asset?.Kind == EWorkspaceAssetKind.Image)
        {
            await OpenWorkspaceAssetAsync(asset);
            if (Editor._activeSource?.Id == asset.Id) await OpenMaterialMapsAsync();
            return;
        }
        if (action == "rename" && asset is { Unreal: null })
        {
            string? name = _workspaceBrowser!.Prompt(Localization.Text("RenameAsset"), Localization.Text("Name"), asset.DisplayName);
            if (name is null || asset.Unreal is not null) return;
            var source = _sources.FirstOrDefault(s => s.Id == asset.Id);
            _workspaceCatalog.RenameLocalAsset(asset, name, _library, source);
            if (source is not null)
            {
                Editor.History.RefreshHistoryFor(source);
                Editor.History.RefreshSourceTray();
            }
            _workspaceBrowser.Refresh(asset.Id);
            return;
        }
        if (action == "create-unreal" && asset?.Kind == EWorkspaceAssetKind.Image)
        {
            await CreateUnrealTextureForAssetAsync(asset);
            return;
        }
        if (action == "move-unreal" && asset?.Unreal is not null)
        {
            MoveUnrealTexture(asset);
            return;
        }
        if (action == "delete-unreal" && asset?.Unreal is not null)
        {
            DeleteUnrealTexture(asset);
            return;
        }
        if (action == "copy" && asset is not null && _workspaceCatalog.State.ProjectId is { } project)
        {
            if (_workspaceCatalog.IsUnrealProject(project)) return;
            if (!await EnsureUnrealImageAsync(asset)) return;
            PersistWorkspaceState();
            var copy = _workspaceCatalog.CopyForProject(asset.Id, project, _library);
            if (copy.Kind == EWorkspaceAssetKind.Image && _library.LoadSource(copy.Id, restoreHistory: false) is { } source)
            {
                source.RestoreSelection(copy.SelectedResult);
                source.DraftSettings = copy.Draft;
                _sources.Add(source);
            }
            SynchronizeWorkspace(); _workspaceBrowser!.Refresh(copy.Id); return;
        }
        if (action == "delete" && asset is not null)
        {
            if (asset.Unreal is { Missing: false }) return;
            if (asset.Unreal is { } binding && UnrealProject(binding.ProjectId) is { } unreal &&
                Mailbox(unreal).Jobs().Any(j => j.Request.AssetId == asset.Id && j.Request.Operation != "read"))
                throw new InvalidOperationException(Localization.Text("ThisTextureHasAnUnfinishedUnreal"));
            if (asset.Kind == EWorkspaceAssetKind.Image && _sources.FirstOrDefault(s => s.Id == asset.Id) is { } source) Editor.RemoveSource(source);
            _workspaceCatalog.ForgetAsset(asset.Id);
            // Orphaned document payloads are retained in the prototype for recovery.
            ShowHome(); _workspaceBrowser!.Refresh(); return;
        }
        if (action == "new-board")
        {
            string? name = _workspaceBrowser!.Prompt(Localization.Text("CreateBoard"), Localization.Text("BoardName"), Localization.Text("NewBoard")); if (string.IsNullOrWhiteSpace(name)) return;
            if (!moodboardWorkspace.CanLeave()) return;
            PersistWorkspaceState();
            ReleaseWorkspaceAsset();
            var board = new WorkspaceAsset { Kind = EWorkspaceAssetKind.Board, Name = name, ContentFile = "content.tboard" };
            string path = _workspaceCatalog.ContentPath(board); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ShowMoodboardWorkspace(); moodboardWorkspace.CreateWorkspaceBoard(1024, 768);
            moodboardWorkspace.WorkspaceSavePath = path;
            try { await moodboardWorkspace.SaveToFileAsync(path); }
            catch { moodboardWorkspace.WorkspaceSavePath = null; throw; }
            _workspaceCatalog.State.Assets.Add(board); AddNewWorkspaceAsset(board); _workspaceActive = board;
            UpdateDocumentPreview(board); return;
        }
        if (asset is null || asset.Kind != EWorkspaceAssetKind.Image) return;
        if (Editor._activeSource?.Id != asset.Id) await OpenWorkspaceAssetAsync(asset);
        if (Editor._activeSource?.Id != asset.Id || Editor._sourceImage is null) return;
        if (action == "background")
        {
            var targetSource = Editor._activeSource;
            Guid? parentId = Editor.ProcessingBasis == 1 && Editor._downscaledResult is not null ? targetSource.SelectedGeneration?.Id : null;
            var settings = Editor.Tools.CaptureSettings();
            ShowBackgroundWorkspace(); backgroundWorkspace.SetImage(Editor.WorkspaceBasis(), asset.Name, Editor.WorkspaceBasisPath());
            backgroundWorkspace.EnableWorkspaceApply(async image =>
            {
                using (image) Editor.SaveWorkspaceImageResult(image, Localization.Text("BackgroundRemoval"), targetSource, parentId, settings, backgroundWorkspace.AutomaticRecipeStep, backgroundWorkspace.HasManualErasure);
                ShowEditor(); await Task.CompletedTask;
            });
        }
        else if (action == "ico") { ShowIconWorkspace(); iconWorkspace.SetImage(Editor.WorkspaceBasis(), asset.Name, Editor.WorkspaceBasisPath()); iconWorkspace.UseWorkspaceExport(); }
        else if (action == "export") ExportWorkspaceImage();
        else if (action == "replace") Editor.Info.OpenColorReplace(System.Drawing.Color.Black, true);
    }

    internal void AddNewWorkspaceAsset(WorkspaceAsset asset)
    {
        if (_workspaceCatalog!.State.ProjectId is { } project && !_workspaceCatalog.IsUnrealProject(project)) _workspaceCatalog.Link(asset.Id, project, _workspaceCatalog.State.FolderId);
        RefreshWorkspaceSources();
        _workspaceCatalog.Save(); _workspaceBrowser!.Refresh(asset.Id);
    }

    private void ExportWorkspaceImage()
    {
        if (Editor._sourceImage is null) return;
        var dialog = new SaveFileDialog { Title = Localization.Text("ExportImage"), Filter = Localization.Text("PNGWithTransparencyFileFilterLabel"), DefaultExt = ".png", OverwritePrompt = true, FileName = Path.GetFileNameWithoutExtension(_workspaceActive?.Name ?? "image") + ".png" };
        if (dialog.ShowDialog(this) != true) return;
        string path = Path.GetFullPath(dialog.FileName);
        EnsureExportPathAllowed(path);
        string temporary = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try { Editor.WorkspaceBasis().Save(temporary, System.Drawing.Imaging.ImageFormat.Png); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        Notify(Localization.Text("ExportedPrefix") + path, NotificationKind.Success);
    }

    internal void SynchronizeWorkspace(SourceEntry? changedSourceImage = null)
    {
        if (!_workspaceReady || _workspaceSyncing || _workspaceCatalog is null) return;
        _workspaceSyncing = true;
        try
        {
            _workspaceCatalog.SyncImages(_sources);
            foreach (var source in _sources)
            {
                var asset = _workspaceCatalog.State.Assets.Single(a => a.Id == source.Id);
                if (_observedSources.Add(source.Id)) source.GenerationsChanged += WorkspaceHistoryChanged;
                if (source == changedSourceImage || source.HasLoadedHistory &&
                    (asset.SelectedResult != source.SelectedResultId || asset.Transparent is null && source == Editor._activeSource))
                    UpdateImageMetadata(asset, source);
            }
            RefreshWorkspaceSources();
            _workspaceCatalog.Save();
            if (_workspaceBrowser?.Visibility == Visibility.Visible) _workspaceBrowser.Refresh();
        }
        catch (Exception ex) { Notify(Localization.Text("CatalogPrefix") + ex.Message, NotificationKind.Error); }
        finally { _workspaceSyncing = false; }
    }

    private void WorkspaceHistoryChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        if (sender is SourceEntry source && args.NewItems is { } items &&
            _workspaceCatalog?.State.Assets.FirstOrDefault(asset => asset.Id == source.Id)?.Unreal is { } binding)
            foreach (GenerationEntry result in items)
            {
                string revision = result.ParentGenerationId is { } parent
                    ? binding.ResultRevisions.GetValueOrDefault(parent, binding.LoadedRevision)
                    : binding.LoadedRevision;
                binding.ResultRevisions.TryAdd(result.Id, revision);
            }
        SynchronizeWorkspace();
    }

    private void UpdateImageMetadata(WorkspaceAsset asset, SourceEntry source)
    {
        var selected = source.SelectedResultId is { } id ? _library.FindGeneration(source, id) : null;
        string path = selected is null ? _library.SourcePath(source) : _library.ResultPath(source, selected);
        var image = PixelEditBuffer.DecodeFile(path);
        asset.Transparent = HasTransparency(image);
        asset.SelectedResult = selected?.Id; asset.Width = image.PixelWidth; asset.Height = image.PixelHeight;
        asset.SetThumbnail(source.PreviewFor(selected?.Id), refresh: true);
        if (selected is not null) asset.ModifiedAt = selected.CreatedAt;
    }

    private void UpdateDocumentPreview(WorkspaceAsset asset)
    {
        if (_workspaceCatalog is null) return;
        string path = _workspaceCatalog.ContentPath(asset), preview = Path.Combine(Path.GetDirectoryName(path)!, "preview.png");
        if (asset.Kind == EWorkspaceAssetKind.Board)
        {
            var board = moodboardWorkspace;
            if (!board.HasBoard || _workspaceActive?.Id != asset.Id) { board = new MoodboardWorkspace(); board.LoadFromFile(path); }
            var bitmap = board.WorkspaceThumbnail();
            asset.Width = board.WorkspaceWidth; asset.Height = board.WorkspaceHeight; asset.Transparent = false;
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(preview); encoder.Save(output);
        }
        if (asset.Kind == EWorkspaceAssetKind.Animation && File.Exists(preview))
            asset.Transparent = HasTransparency(PixelEditBuffer.DecodeFile(preview));
        _library.Resources.Invalidate(preview);
        SetDocumentThumbnail(asset, refresh: true);
    }

    private void SetDocumentThumbnail(WorkspaceAsset asset, bool refresh = false)
    {
        string path = _workspaceCatalog!.ContentPath(asset);
        string preview = Path.Combine(Path.GetDirectoryName(path)!, "preview.png");
        asset.SetThumbnail(new ThumbnailResource(_library.Resources, preview, 180, 128), refresh);
    }

    private static bool HasTransparency(BitmapSource image)
    {
        if (image.Format != PixelFormats.Bgra32) image = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var row = new byte[checked(image.PixelWidth * 4)];
        for (int y = 0; y < image.PixelHeight; y++)
        {
            image.CopyPixels(new Int32Rect(0, y, image.PixelWidth, 1), row, row.Length, 0);
            for (int q = 3; q < row.Length; q += 4) if (row[q] < 255) return true;
        }
        return false;
    }

    private bool? ResolveWorkspaceTransparency(WorkspaceAsset asset)
    {
        try
        {
            if (asset.Kind != EWorkspaceAssetKind.Image) return null;
            if (_sources.FirstOrDefault(source => source.Id == asset.Id) is not { } source) return null;
            string original = _library.SourcePath(source);
            string path = asset.SelectedResult is { } result ? _library.ResultPath(source, result) : original;
            return HasTransparency(PixelEditBuffer.DecodeFile(File.Exists(path) ? path : original));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { return null; }
    }

    private void PersistWorkspaceState()
    {
        if (!_workspaceReady || _workspaceCatalog is null) return;
        foreach (var window in _materialMapWindows.Values.ToArray()) window.SaveWorkspace();
        if (_workspaceActive is { Kind: EWorkspaceAssetKind.Animation } animation && asepriteWorkspace.Visibility == Visibility.Visible)
        {
            asepriteWorkspace.CaptureWorkspaceSettings(animation);
            if (asepriteWorkspace.CurrentSheetPath is { } sheet)
            {
                string preview = Path.Combine(Path.GetDirectoryName(_workspaceCatalog.ContentPath(animation))!, "preview.png"); File.Copy(sheet, preview, true);
                var image = PixelEditBuffer.DecodeFile(preview); animation.Width = image.PixelWidth; animation.Height = image.PixelHeight; UpdateDocumentPreview(animation);
            }
        }
        if (Editor._activeSource is not null && _workspaceCatalog.State.Assets.FirstOrDefault(a => a.Id == Editor._activeSource.Id) is { } asset)
        {
            asset.Draft = Editor.Tools.CaptureSettings(); asset.SelectedResult = Editor._activeSource.SelectedGeneration?.Id;
            asset.Basis = Editor.ProcessingBasis; asset.Zoom = Editor.Preview.PreviewZoom; asset.Background = Editor.Preview.PreviewBackground;
            Editor._activeSource.DraftSettings = asset.Draft;
        }
        _workspaceCatalog.Save();
    }

    private void ShowWorkspaceBrowser()
    {
        if (_workspaceBrowser is null) return;
        PersistWorkspaceState(); _workspaceBrowser.Visibility = Visibility.Visible;
        SynchronizeWorkspace(); _workspaceBrowser.Refresh();
        statusLabel.Text = "";
        UpdateNavigationState();
    }

    private void HideWorkspaceBrowser()
    {
        if (_workspaceBrowser is not null)
        {
            _workspaceBrowser.Visibility = Visibility.Collapsed;
            _workspaceBrowser.ReleaseCards();
        }
        UpdateNavigationState();
    }

    private bool ChangeWorkspaceScope(string view, Guid? project)
    {
        if (WorkspaceBusy || _workspaceCatalog is null) return false;
        var previousWindows = _materialMapWindows.Where(entry => !IsAssetInScope(entry.Key.Source, view, project)).ToArray();
        if (previousWindows.Any(entry => entry.Value.IsBusy)) return false;
        if (_workspaceActive is { } active && !IsAssetInScope(active.Id, view, project) && !ShowHome()) return false;
        foreach (var entry in previousWindows)
        {
            entry.Value.Close();
            if (_materialMapWindows.ContainsKey(entry.Key)) return false;
        }
        _unrealOpening = null;
        _library.Resources.Clear();
        return true;
    }

    internal bool IsAssetInScope(Guid asset, string view, Guid? project)
    {
        var links = _workspaceCatalog!.State.Links;
        return view switch
        {
            "project" => links.Any(link => link.ProjectId == project && link.AssetId == asset),
            "unassigned" => links.All(link => link.AssetId != asset),
            _ => true
        };
    }

    private void ReleaseWorkspaceAsset()
    {
        Editor.Dock.CloseEditorTools();
        Editor.ReleaseImageEditor();
        asepriteWorkspace.Clear();
        moodboardWorkspace.ReleaseDocument();
        iconWorkspace.ReleaseImage();
        backgroundWorkspace.ReleaseImage();
        _workspaceActive = null;
        _workspaceSourceProject = null;
        _workspaceSourcesUnassigned = false;
        _workspaceSources.Clear();
        Editor.History.ClearSourceTray();
    }

    internal void AdoptWorkspaceSource(SourceEntry source)
    {
        if (!_workspaceReady || _workspaceActive?.Id == source.Id) return;
        PersistWorkspaceState();
        _workspaceActive = _workspaceCatalog!.State.Assets.FirstOrDefault(a => a.Id == source.Id);
        if (_workspaceActive is { } asset) SetWorkspaceSourceScope(asset);
    }

    private void SetWorkspaceSourceScope(WorkspaceAsset asset)
    {
        var state = _workspaceCatalog!.State;
        _workspaceSourceProject = state.View == "project" && state.ProjectId is { } project && state.Links.Any(l => l.ProjectId == project && l.AssetId == asset.Id) ? project : null;
        _workspaceSourcesUnassigned = state.View == "unassigned" && !state.Links.Any(l => l.AssetId == asset.Id);
        RefreshWorkspaceSources();
    }

    private void RefreshWorkspaceSources()
    {
        if (!_workspaceReady || _workspaceCatalog is null || _workspaceActive is not { Kind: EWorkspaceAssetKind.Image }) return;
        var links = _workspaceCatalog.State.Links;
        var members = links.Where(l => _workspaceSourceProject is null || l.ProjectId == _workspaceSourceProject).Select(l => l.AssetId).ToHashSet();
        var mapIds = _workspaceCatalog.MaterialMapAssetIds();
        var sources = _sources.Where(s => !mapIds.Contains(s.Id) && (_workspaceSourceProject is not null
            ? members.Contains(s.Id) : !_workspaceSourcesUnassigned || !members.Contains(s.Id))).ToArray();
        if (_workspaceSources.SequenceEqual(sources)) { Editor.History.RefreshSourceTray(); return; }
        Editor.History.WithSelectionPaused(() =>
        {
            _workspaceSources.Clear();
            foreach (var source in sources) _workspaceSources.Add(source);
            Editor.History.RefreshSourceTray();
        });
    }
}
