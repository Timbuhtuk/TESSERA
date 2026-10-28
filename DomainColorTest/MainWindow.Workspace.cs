using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class MainWindow
{
    private WorkspaceCatalog? _workspaceCatalog;
    private WorkspaceBrowser? _workspaceBrowser;
    private WorkspaceAsset? _workspaceActive;
    private Border? _workspaceDock;
    private readonly List<GroupBox> _workspaceGroups = [];
    private readonly HashSet<Guid> _observedSources = [];
    private readonly ObservableCollection<SourceEntry> _workspaceSources = [];
    private Guid? _workspaceSourceProject;
    private bool _workspaceSourcesUnassigned;
    private bool _workspaceReady, _workspaceSyncing, _workspaceImporting;
    public WorkspaceCatalog? Workspace => _workspaceCatalog;
    public WorkspaceBrowser? Browser => _workspaceBrowser;

    public void EnableWorkspacePrototype(bool enableUpdates = false)
    {
        WorkspaceBrushSafety.Register();
        _autoCheckUpdates = enableUpdates;
        _workspaceCatalog = new WorkspaceCatalog(_library.DirectoryPath);
        foreach (var source in _sources)
            if (_workspaceCatalog.State.Assets.FirstOrDefault(a => a.Id == source.Id) is { } saved)
            {
                source.SelectedGeneration = source.Generations.FirstOrDefault(g => g.Id == saved.SelectedResult);
                source.DraftSettings = saved.Draft;
            }
        _activeSource = null;
        _workspaceCatalog.SyncImages(_sources);
        Title = enableUpdates ? "TESSERA" : "TESSERA · Workspace prototype";
        _workspaceBrowser = new WorkspaceBrowser();
        Grid.SetRow(_workspaceBrowser, 1); rootLayout.Children.Add(_workspaceBrowser);
        _workspaceBrowser.ImportRequested += async paths => await ImportWorkspaceFilesAsync(paths);
        _workspaceBrowser.OpenRequested += async asset => await OpenWorkspaceAssetAsync(asset);
        _workspaceBrowser.ActionRequested += async (action, asset) =>
        {
            try { await WorkspaceActionAsync(action, asset); }
            catch (Exception ex) { WorkspaceError(ex); }
        };
        _workspaceBrowser.StatusChanged += text => statusLabel.Text = text;
        asepriteWorkspace.WorkspaceImport = async paths =>
        {
            await ImportWorkspaceFilesAsync(paths);
            var imported = _workspaceCatalog.State.Assets.LastOrDefault(a => a.Kind == WorkspaceAssetKind.Animation);
            if (imported is not null) await OpenWorkspaceAssetAsync(imported);
        };
        _workspaceBrowser.Resources.MergedDictionaries.Add(Resources);
        _workspaceBrowser.Bind(_workspaceCatalog);
        workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition());
        workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        workspaceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        workspaceGrid.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(previewArea, 1);
        _workspaceDock = new Border { Width = 320, BorderThickness = new Thickness(1, 0, 0, 0), Visibility = Visibility.Collapsed };
        _workspaceDock.SetResourceReference(Border.BackgroundProperty, "Ts.Raised");
        _workspaceDock.SetResourceReference(Border.BorderBrushProperty, "Ts.Border");
        Grid.SetColumn(_workspaceDock, 1); Grid.SetRow(_workspaceDock, 1); workspaceGrid.Children.Add(_workspaceDock);
        InitializeToolDock();
        colorToolGroup.Header = Localization.Text("Палитра");
        colorOnlyButton.Content = Localization.Text("Создать результат");
        resultPreview.EmptyText = Localization.Text("Выберите действие справа.\nНовый вариант появится здесь и сохранится в истории.");
        updateButton.Content = enableUpdates ? UpdateButtonText() : Localization.Text("Экспериментальная версия");
        updateButton.IsEnabled = checkUpdatesMenuItem.IsEnabled = enableUpdates;
        SizeChanged += (_, _) => UpdateWorkspaceDockLayout();
        Closing += (_, e) =>
        {
            if (e.Cancel) return;
            try { PersistWorkspaceState(); } catch (Exception ex) { e.Cancel = true; WorkspaceError(ex); }
        };
        _sources.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null) foreach (SourceEntry source in e.OldItems) _workspaceCatalog.ForgetAsset(source.Id);
            SynchronizeWorkspace();
        };
        moodboardWorkspace.ProjectSaved += path =>
        {
            if (_workspaceActive is not { Kind: WorkspaceAssetKind.Board } asset || _workspaceCatalog.ContentPath(asset) != path) return;
            asset.ModifiedAt = DateTimeOffset.Now;
            UpdateDocumentPreview(asset); _workspaceCatalog.Save();
        };
        foreach (var document in _workspaceCatalog.State.Assets.Where(a => a.Kind != WorkspaceAssetKind.Image)) UpdateDocumentPreview(document);
        _restoringHistory = true;
        try { sourceStrip.ItemsSource = _traySources; }
        finally { _restoringHistory = false; }
        _workspaceReady = true; SynchronizeWorkspace(); ShowHome();
    }

    private bool WorkspaceBusy => _processing || _workspaceImporting || iconWorkspace.IsBusy || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy || moodboardWorkspace.IsBusy;
    private void WorkspaceError(Exception ex) { statusLabel.Text = ex.Message; TesseraDialog.Show(this, ex.Message, "Tessera", MessageBoxButton.OK, MessageBoxImage.Warning); }

    public async Task ImportWorkspaceFilesAsync(IReadOnlyList<string> paths)
    {
        if (_workspaceCatalog is null || WorkspaceBusy) return;
        _workspaceImporting = true;
        _workspaceBrowser!.IsEnabled = false;
        UpdateBusyIndicator();
        try
        {
            var failures = new List<string>(); var imported = new List<WorkspaceAsset>();
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                WorkspaceAsset? pendingDocument = null;
                try
                {
                    WorkspaceAsset asset;
                    if (Path.GetExtension(path).ToLowerInvariant() is ".ase" or ".aseprite" or ".tboard" or ".ttiles")
                    {
                        asset = _workspaceCatalog.ImportDocument(path);
                        pendingDocument = asset;
                        if (asset.Kind == WorkspaceAssetKind.Animation)
                        {
                            string previewFolder = Path.Combine(Path.GetDirectoryName(_workspaceCatalog.ContentPath(asset))!, "preview-build");
                            var converted = await Task.Run(() => PixelArtAseprite.AsepriteConverter.Convert(_workspaceCatalog.ContentPath(asset), previewFolder, new PixelArtAseprite.AsepriteExportOptions()));
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
                    if (_workspaceCatalog.State.ProjectId is { } project) _workspaceCatalog.Link(asset.Id, project, _workspaceCatalog.State.FolderId);
                    imported.Add(asset);
                }
                catch (Exception ex) { if (pendingDocument is not null) _workspaceCatalog.ForgetAsset(pendingDocument.Id); failures.Add(Path.GetFileName(path) + ": " + ex.Message); }
            }
            RefreshWorkspaceSources();
            _workspaceCatalog.Save(); _workspaceBrowser!.Refresh(imported.LastOrDefault()?.Id);
            statusLabel.Text = Localization.Format("Добавлено: {0}", imported.Count) + (failures.Count > 0 ? Localization.Text(" · Ошибки: ") + string.Join("; ", failures) : Localization.Text(" · Выберите материал для работы"));
        }
        finally { _workspaceImporting = false; _workspaceBrowser.IsEnabled = true; UpdateBusyIndicator(); }
    }

    private async Task PromptWorkspaceFilesAsync()
    {
        var dialog = new OpenFileDialog { Title = Localization.Text("Добавить материалы"), Multiselect = true, Filter = Localization.Text("Материалы Tessera|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ase;*.aseprite;*.tboard;*.ttiles") };
        if (dialog.ShowDialog(this) == true) await ImportWorkspaceFilesAsync(dialog.FileNames);
    }

    public async Task OpenWorkspaceAssetAsync(WorkspaceAsset asset)
    {
        if (_workspaceCatalog is null || WorkspaceBusy) return;
        if (asset.Kind == WorkspaceAssetKind.Board && moodboardWorkspace.HasBoard && moodboardWorkspace.WorkspaceSavePath != _workspaceCatalog.ContentPath(asset) && !moodboardWorkspace.CanLeave()) return;
        try
        {
            PersistWorkspaceState(); _workspaceActive = asset; asset.OpenedAt = DateTimeOffset.Now;
            if (asset.Kind == WorkspaceAssetKind.Image)
            {
                var source = _sources.Single(s => s.Id == asset.Id);
                SetWorkspaceSourceScope(asset);
                if (asset.SelectedResult is { } result) source.SelectedGeneration = source.Generations.FirstOrDefault(g => g.Id == result) ?? source.SelectedGeneration;
                SelectSource(source);
                if (source.SelectedGeneration is { } selected) SelectGeneration(selected);
                if (asset.Draft is { } draft) RestoreSettings(draft);
                ProcessingBasis = asset.Basis == 1 && _downscaledResult is not null ? 1 : 0;
                PreviewZoom = Math.Clamp(asset.Zoom, 0, ZoomFactors.Length - 1);
                PreviewBackground = Math.Clamp(asset.Background, 0, 3);
                OpenDockedWorkspaceTool("size", Localization.Text("Размер"), sizeToolGroup, frameToolGroup, pixelToolGroup);
            }
            else if (asset.Kind == WorkspaceAssetKind.Animation)
            {
                ShowAsepriteWorkspace(); asepriteWorkspace.Clear(); asepriteWorkspace.ApplyWorkspaceSettings(asset);
                await asepriteWorkspace.AddFilesAsync([_workspaceCatalog.ContentPath(asset)]);
                PersistWorkspaceState();
            }
            else if (asset.Kind == WorkspaceAssetKind.Board)
            {
                ShowMoodboardWorkspace(); await moodboardWorkspace.LoadFromFileAsync(_workspaceCatalog.ContentPath(asset));
                moodboardWorkspace.WorkspaceSavePath = _workspaceCatalog.ContentPath(asset);
            }
            else await OpenWorkspaceTilesAsync(asset);
            _workspaceCatalog.State.SelectedAsset = asset.Id; _workspaceCatalog.Save(); UpdateNavigationState();
        }
        catch (Exception ex) { WorkspaceError(ex); }
    }

    private async Task WorkspaceActionAsync(string action, WorkspaceAsset? asset)
    {
        if (_workspaceCatalog is null || WorkspaceBusy) return;
        if (action == "copy" && asset is not null && _workspaceCatalog.State.ProjectId is { } project)
        {
            PersistWorkspaceState();
            var copy = _workspaceCatalog.CopyForProject(asset.Id, project, _library);
            foreach (var source in _library.Load().Where(s => !_sources.Any(existing => existing.Id == s.Id)))
            {
                var saved = _workspaceCatalog.State.Assets.First(a => a.Id == source.Id);
                source.SelectedGeneration = source.Generations.FirstOrDefault(g => g.Id == saved.SelectedResult);
                source.DraftSettings = saved.Draft;
                _sources.Add(source);
            }
            SynchronizeWorkspace(); _workspaceBrowser!.Refresh(copy.Id); return;
        }
        if (action == "delete" && asset is not null)
        {
            if (asset.Kind == WorkspaceAssetKind.Image) RemoveSource(_sources.Single(s => s.Id == asset.Id));
            _workspaceCatalog.ForgetAsset(asset.Id);
            // Orphaned document payloads are retained in the prototype for recovery.
            ShowHome(); _workspaceBrowser!.Refresh(); return;
        }
        if (action == "new-board")
        {
            string? name = _workspaceBrowser!.Prompt(Localization.Text("Создать доску"), Localization.Text("Название доски"), Localization.Text("Новая доска")); if (string.IsNullOrWhiteSpace(name)) return;
            if (moodboardWorkspace.HasBoard && !moodboardWorkspace.CanLeave()) return;
            var board = new WorkspaceAsset { Kind = WorkspaceAssetKind.Board, Name = name, ContentFile = "content.tboard" };
            string path = _workspaceCatalog.ContentPath(board); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ShowMoodboardWorkspace(); moodboardWorkspace.CreateWorkspaceBoard(1024, 768); await moodboardWorkspace.SaveToFileAsync(path);
            _workspaceCatalog.State.Assets.Add(board); AddNewWorkspaceAsset(board); _workspaceActive = board; moodboardWorkspace.WorkspaceSavePath = path;
            UpdateDocumentPreview(board); return;
        }
        if (action == "new-tiles")
        {
            string? name = _workspaceBrowser!.Prompt(Localization.Text("Создать коллекцию тайлов"), Localization.Text("Название коллекции"), Localization.Text("Новые тайлы")); if (string.IsNullOrWhiteSpace(name)) return;
            var tiles = new WorkspaceAsset { Kind = WorkspaceAssetKind.Tiles, Name = name, ContentFile = "content.ttiles" };
            string path = _workspaceCatalog.ContentPath(tiles); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            TileCollectionFile.Save(path, [], new()); _workspaceCatalog.State.Assets.Add(tiles); AddNewWorkspaceAsset(tiles); _workspaceActive = tiles; await OpenWorkspaceTilesAsync(tiles); return;
        }
        if (asset is null || asset.Kind != WorkspaceAssetKind.Image) return;
        if (_activeSource?.Id != asset.Id) await OpenWorkspaceAssetAsync(asset);
        if (_activeSource is null || _sourceImage is null) return;
        if (action == "background")
        {
            var targetSource = _activeSource;
            Guid? parentId = ProcessingBasis == 1 && _downscaledResult is not null ? targetSource.SelectedGeneration?.Id : null;
            var settings = CaptureSettings();
            ShowBackgroundWorkspace(); backgroundWorkspace.SetImage(WorkspaceBasis(), asset.Name, WorkspaceBasisPath());
            backgroundWorkspace.EnableWorkspaceApply(async image =>
            {
                using (image) SaveWorkspaceImageResult(image, Localization.Text("Удаление фона"), targetSource, parentId, settings, backgroundWorkspace.AutomaticRecipeStep, backgroundWorkspace.HasManualErasure);
                ShowEditor(); await Task.CompletedTask;
            });
        }
        else if (action == "ico") { ShowIconWorkspace(); iconWorkspace.SetImage(WorkspaceBasis(), asset.Name, WorkspaceBasisPath()); iconWorkspace.UseWorkspaceExport(); }
        else if (action == "tiles") await OpenWorkspaceTilesAsync(asset);
        else if (action == "board")
        {
            string path = WorkspaceBasisPath();
            var boards = _workspaceCatalog.State.Assets.Where(a => a.Kind == WorkspaceAssetKind.Board).ToList();
            string? choice = _workspaceBrowser!.ChooseBoard(boards); if (choice is null) return;
            var board = boards.Single(a => a.Id.ToString() == choice);
            await OpenWorkspaceAssetAsync(board); await moodboardWorkspace.AddFilesAsync([path]);
        }
        else if (action == "export") ExportWorkspaceImage();
        else if (action == "replace") OpenColorReplace(System.Drawing.Color.Black, true);
    }

    private void AddNewWorkspaceAsset(WorkspaceAsset asset)
    {
        if (_workspaceCatalog!.State.ProjectId is { } project) _workspaceCatalog.Link(asset.Id, project, _workspaceCatalog.State.FolderId);
        RefreshWorkspaceSources();
        _workspaceCatalog.Save(); _workspaceBrowser!.Refresh(asset.Id);
    }

    private Bitmap WorkspaceBasis() => ProcessingBasis == 1 && _downscaledResult is not null ? _downscaledResult : _sourceImage!;
    private string WorkspaceBasisPath() => ProcessingBasis == 1 && _activeSource!.SelectedGeneration is { } selected ? _library.ResultPath(_activeSource, selected) : _library.SourcePath(_activeSource!);
    private void SaveWorkspaceImageResult(Bitmap result, string operation, SourceEntry target, Guid? parentId, EditorSettings settings, ProcessingStep? step = null, bool manual = false)
    {
        if (!_sources.Contains(target)) throw new InvalidOperationException(Localization.Text("Исходный ассет больше не доступен."));
        var entry = _library.SaveGeneration(target, result, null, new GenerationEntry { Operation = operation, Caption = operation, ParentGenerationId = parentId, PreservesTransparency = true, Settings = settings,
            OperationId = manual ? "manual.erase" : step?.OperationId, AppliedRecipe = step is null || manual ? null : HistoryRecipes.Single(step),
            RecipeUnavailableReason = manual ? Localization.Text("Цепочка содержит ручной ластик. Этот шаг нельзя воспроизвести макросом.") : step is null ? Localization.Text("Параметры операции не сохранены.") : null });
        target.Generations.Add(entry); target.SelectedGeneration = entry;
        if (target == _activeSource) { SelectGeneration(entry); ProcessingBasis = 1; }
        SynchronizeWorkspace();
    }
    private void ExportWorkspaceImage()
    {
        if (_sourceImage is null) return;
        var dialog = new SaveFileDialog { Title = Localization.Text("Экспортировать изображение"), Filter = Localization.Text("PNG с прозрачностью|*.png"), DefaultExt = ".png", FileName = Path.GetFileNameWithoutExtension(_workspaceActive?.Name ?? "image") + ".png" };
        if (dialog.ShowDialog(this) != true) return;
        string path = Path.GetFullPath(dialog.FileName);
        EnsureExportPathAllowed(path);
        WorkspaceBasis().Save(path, System.Drawing.Imaging.ImageFormat.Png); statusLabel.Text = Localization.Text("Экспортировано: ") + path;
    }

    private async Task OpenWorkspaceTilesAsync(WorkspaceAsset asset)
    {
        var inspector = GetTileInspector();
        if (!inspector.RefreshWorkspaceMaterials()) return;
        if (asset.Kind == WorkspaceAssetKind.Tiles)
        {
            if (!inspector.OpenWorkspaceCollection(_workspaceCatalog!.ContentPath(asset))) return;
        }
        else
        {
            await inspector.AddWorkspaceMaterialAsync(asset.Id, ProcessingBasis == 1 && _activeSource?.SelectedGeneration is not null);
        }
        ShowTileInspector(inspector);
    }

    private void SynchronizeWorkspace()
    {
        if (!_workspaceReady || _workspaceSyncing || _workspaceCatalog is null) return;
        _workspaceSyncing = true;
        try
        {
            _workspaceCatalog.SyncImages(_sources);
            foreach (var source in _sources)
            {
                if (_observedSources.Add(source.Id)) source.Generations.CollectionChanged += (_, _) => SynchronizeWorkspace();
                var asset = _workspaceCatalog.State.Assets.Single(a => a.Id == source.Id);
                if (asset.Transparent is null || asset.SelectedResult != source.SelectedGeneration?.Id) UpdateImageMetadata(asset, source);
            }
            RefreshWorkspaceSources();
            _workspaceCatalog.Save();
            if (_workspaceBrowser?.Visibility == Visibility.Visible) _workspaceBrowser.Refresh();
        }
        catch (Exception ex) { statusLabel.Text = Localization.Text("Каталог: ") + ex.Message; }
        finally { _workspaceSyncing = false; }
    }

    private void UpdateImageMetadata(WorkspaceAsset asset, SourceEntry source)
    {
        string path = source.SelectedGeneration is { } result ? _library.ResultPath(source, result) : _library.SourcePath(source);
        var image = TileAsset.DecodeFile(path);
        var pixels = new byte[checked(image.PixelWidth * image.PixelHeight * 4)]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        asset.Transparent = false;
        for (int q = 3; q < pixels.Length; q += 4) if (pixels[q] < 255) { asset.Transparent = true; break; }
        asset.SelectedResult = source.SelectedGeneration?.Id; asset.Width = image.PixelWidth; asset.Height = image.PixelHeight;
        asset.Thumbnail = source.SelectedGeneration?.Thumbnail ?? source.Thumbnail;
        if (source.SelectedGeneration is { } selected) asset.ModifiedAt = selected.CreatedAt;
    }

    private void UpdateDocumentPreview(WorkspaceAsset asset)
    {
        if (_workspaceCatalog is null) return;
        string path = _workspaceCatalog.ContentPath(asset), preview = Path.Combine(Path.GetDirectoryName(path)!, "preview.png");
        if (asset.Kind == WorkspaceAssetKind.Tiles)
        {
            var loaded = TileCollectionFile.Load(path); var first = loaded.Assets.FirstOrDefault();
            if (first is not null) { asset.Width = first.Width; asset.Height = first.Height; asset.Thumbnail = first.Image; asset.Transparent = HasTransparency(first.Image); using var output = File.Create(preview); first.WritePng(output); }
        }
        else if (asset.Kind == WorkspaceAssetKind.Board)
        {
            var board = moodboardWorkspace;
            if (!board.HasBoard || _workspaceActive?.Id != asset.Id) { board = new MoodboardWorkspace(); board.LoadFromFile(path); }
            var bitmap = board.WorkspaceThumbnail(); asset.Thumbnail = bitmap;
            asset.Width = board.WorkspaceWidth; asset.Height = board.WorkspaceHeight; asset.Transparent = false;
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(preview); encoder.Save(output);
        }
        if (File.Exists(preview))
        {
            if (asset.Kind == WorkspaceAssetKind.Animation) asset.Transparent = HasTransparency(TileAsset.DecodeFile(preview));
            using var input = File.OpenRead(preview);
            var frame = BitmapFrame.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (frame.PixelWidth <= 180 && frame.PixelHeight <= 128) { frame.Freeze(); asset.Thumbnail = frame; }
            else
            {
                input.Position = 0; var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                if (frame.PixelWidth >= frame.PixelHeight) image.DecodePixelWidth = 180; else image.DecodePixelHeight = 128;
                image.StreamSource = input; image.EndInit(); image.Freeze(); asset.Thumbnail = image;
            }
        }
    }

    private static bool HasTransparency(BitmapSource image)
    {
        if (image.Format != PixelFormats.Bgra32) image = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[checked(image.PixelWidth * image.PixelHeight * 4)]; image.CopyPixels(bytes, image.PixelWidth * 4, 0);
        for (int q = 3; q < bytes.Length; q += 4) if (bytes[q] < 255) return true;
        return false;
    }

    private void PersistWorkspaceState()
    {
        if (!_workspaceReady || _workspaceCatalog is null) return;
        foreach (var window in _materialMapWindows.Values.ToArray()) window.SaveWorkspace();
        if (_workspaceActive is { Kind: WorkspaceAssetKind.Animation } animation && asepriteWorkspace.Visibility == Visibility.Visible)
        {
            asepriteWorkspace.CaptureWorkspaceSettings(animation);
            if (asepriteWorkspace.CurrentSheetPath is { } sheet)
            {
                string preview = Path.Combine(Path.GetDirectoryName(_workspaceCatalog.ContentPath(animation))!, "preview.png"); File.Copy(sheet, preview, true);
                var image = TileAsset.DecodeFile(preview); animation.Width = image.PixelWidth; animation.Height = image.PixelHeight; UpdateDocumentPreview(animation);
            }
        }
        if (_activeSource is not null && _workspaceCatalog.State.Assets.FirstOrDefault(a => a.Id == _activeSource.Id) is { } asset)
        {
            asset.Draft = CaptureSettings(); asset.SelectedResult = _activeSource.SelectedGeneration?.Id;
            asset.Basis = ProcessingBasis; asset.Zoom = PreviewZoom; asset.Background = PreviewBackground;
            _activeSource.DraftSettings = asset.Draft;
        }
        _workspaceCatalog.Save();
    }

    private void ShowWorkspaceBrowser()
    {
        if (_workspaceBrowser is null) return;
        PersistWorkspaceState(); _workspaceBrowser.Visibility = Visibility.Visible;
        SynchronizeWorkspace(); _workspaceBrowser.Refresh();
        statusLabel.Text = Localization.Text("Библиотека · материалы, проекты и общие результаты");
        UpdateNavigationState();
    }
    private void HideWorkspaceBrowser()
    {
        if (_workspaceBrowser is not null) _workspaceBrowser.Visibility = Visibility.Collapsed;
        UpdateNavigationState();
    }

    private void AdoptWorkspaceSource(SourceEntry source)
    {
        if (!_workspaceReady || _workspaceActive?.Id == source.Id) return;
        PersistWorkspaceState();
        _workspaceActive = _workspaceCatalog!.State.Assets.FirstOrDefault(a => a.Id == source.Id);
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
        if (!_workspaceReady || _workspaceCatalog is null) return;
        var links = _workspaceCatalog.State.Links;
        var members = links.Where(l => _workspaceSourceProject is null || l.ProjectId == _workspaceSourceProject).Select(l => l.AssetId).ToHashSet();
        var sources = _sources.Where(s => _workspaceSourceProject is not null ? members.Contains(s.Id) : !_workspaceSourcesUnassigned || !members.Contains(s.Id)).ToArray();
        if (_workspaceSources.SequenceEqual(sources)) { RefreshSourceTray(); return; }
        bool restoring = _restoringHistory;
        _restoringHistory = true;
        try
        {
            _workspaceSources.Clear();
            foreach (var source in sources) _workspaceSources.Add(source);
            RefreshSourceTray();
        }
        finally { _restoringHistory = restoring; }
    }

    private void RestoreWorkspaceImageView(SourceEntry source)
    {
        if (!_workspaceReady || _workspaceCatalog?.State.Assets.FirstOrDefault(a => a.Id == source.Id) is not { } asset) return;
        if (source.DraftSettings is { } draft) RestoreSettings(draft);
        ProcessingBasis = asset.Basis == 1 && _downscaledResult is not null ? 1 : 0;
        PreviewZoom = Math.Clamp(asset.Zoom, 0, ZoomFactors.Length - 1);
        PreviewBackground = Math.Clamp(asset.Background, 0, 3);
        if (_workspaceDock?.Visibility != Visibility.Visible) OpenDockedWorkspaceTool("size", Localization.Text("Размер"), sizeToolGroup, frameToolGroup, pixelToolGroup);
    }

    private void OpenDockedWorkspaceTool(string key, string title, params GroupBox[] groups) => DockTool(key, title, groups);
    private void CloseDockedWorkspaceTool()
    {
        foreach (string key in _dockedTools.Keys.ToArray()) CloseDockedTool(key);
    }
    private void UpdateWorkspaceDockLayout()
    {
        if (_workspaceDock is null) return;
        bool compact = ActualWidth < 1000;
        _workspaceDock.Width = compact ? double.NaN : 320; _workspaceDock.MaxHeight = compact ? 260 : double.PositiveInfinity;
        Grid.SetColumn(_workspaceDock, compact ? 0 : 1); Grid.SetRow(_workspaceDock, compact ? 0 : 1);
        workspaceGrid.ColumnDefinitions[1].Width = new GridLength(compact || _workspaceDock.Visibility != Visibility.Visible ? 0 : 320);
    }
}

public static class WorkspacePrototypeLocation
{
    public static string Prepare(string? applicationDirectory = null, string? appDataDirectory = null)
    {
        string local = appDataDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string root = Path.Combine(applicationDirectory ?? AppContext.BaseDirectory, "WorkspaceData", "Library");
        string source = LibraryLocation.Prepare(local);
        string marker = Path.Combine(root, ".copied-library");
        if (!File.Exists(marker))
        {
            Directory.CreateDirectory(root);
            if (Directory.Exists(source))
            {
                foreach (string folder in Directory.EnumerateDirectories(source))
                    if (Guid.TryParseExact(Path.GetFileName(folder), "N", out _)) CopyDirectory(folder, Path.Combine(root, Path.GetFileName(folder)));
            }
            File.WriteAllText(marker, "Independent prototype library");
        }
        return root;
    }
    private static void CopyDirectory(string source, string target)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) return;
        Directory.CreateDirectory(target);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            string destination = Path.Combine(target, Path.GetFileName(file));
            if (!File.Exists(destination) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) File.Copy(file, destination, false);
        }
        foreach (string folder in Directory.EnumerateDirectories(source)) CopyDirectory(folder, Path.Combine(target, Path.GetFileName(folder)));
    }
}
