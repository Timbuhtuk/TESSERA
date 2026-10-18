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
    private StackPanel? _workspaceToolContent;
    private WrapPanel? _workspaceActions;
    private readonly List<GroupBox> _workspaceGroups = [];
    private readonly HashSet<Guid> _observedSources = [];
    private bool _workspaceReady, _workspaceSyncing, _workspaceImporting;
    public WorkspaceCatalog? Workspace => _workspaceCatalog;
    public WorkspaceBrowser? Browser => _workspaceBrowser;

    public void EnableWorkspacePrototype()
    {
        WorkspaceBrushSafety.Register();
        _autoCheckUpdates = false;
        _workspaceCatalog = new WorkspaceCatalog(_library.DirectoryPath);
        foreach (var source in _sources)
            if (_workspaceCatalog.State.Assets.FirstOrDefault(a => a.Id == source.Id) is { } saved)
            {
                source.SelectedGeneration = source.Generations.FirstOrDefault(g => g.Id == saved.SelectedResult);
                source.DraftSettings = saved.Draft;
            }
        _activeSource = null;
        _workspaceCatalog.SyncImages(_sources);
        Title = "TESSERA · Workspace prototype";
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
        _workspaceActions = new WrapPanel { Margin = new Thickness(18, 0, 18, 8) };
        var parent = (StackPanel)editorToolbar.Parent; parent.Children.Add(_workspaceActions);
        foreach (var pair in new[] { ("Удалить фон", "background"), ("Проверить тайлы", "tiles"), ("Экспорт ICO", "ico"), ("На доску…", "board"), ("Экспорт PNG…", "export"), ("Заменить цвет", "replace") })
        {
            var button = new Button { Content = pair.Item1, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(9, 5, 9, 5) };
            button.Click += async (_, _) => { try { await WorkspaceActionAsync(pair.Item2, _workspaceActive); } catch (Exception ex) { WorkspaceError(ex); } };
            _workspaceActions.Children.Add(button);
        }
        profileToolButton.Content = compactProfileToolButton.Content = "Готовые сценарии";
        colorToolButton.Content = compactColorToolButton.Content = "Число цветов";
        smoothingToolButton.Content = compactSmoothingToolButton.Content = "Очистить цвета";
        infoToolButton.Content = compactInfoToolButton.Content = "Цвета и сведения";
        colorToolGroup.Header = "Число цветов";
        colorOnlyButton.Content = "Уменьшить число цветов";
        resultPreview.EmptyText = "Выберите действие справа.\nНовый вариант появится здесь и сохранится в истории.";
        saveButton.Content = "Экспортировать PNG…";
        updateButton.Content = "Экспериментальная версия"; updateButton.IsEnabled = false;
        checkUpdatesMenuButton.IsEnabled = false;
        SizeChanged += (_, _) => UpdateWorkspaceDockLayout();
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Handled) return;
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            if (e.Key == Key.O && _workspaceBrowser.Visibility == Visibility.Visible) { e.Handled = true; await PromptWorkspaceFilesAsync(); }
            else if (e.Key == Key.S && moodboardWorkspace.Visibility == Visibility.Visible && moodboardWorkspace.HasBoard)
            { e.Handled = true; try { await moodboardWorkspace.SaveWorkspaceAsync(); } catch (Exception ex) { WorkspaceError(ex); } }
        };
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
            _workspaceCatalog.Save(); _workspaceBrowser!.Refresh(imported.LastOrDefault()?.Id);
            statusLabel.Text = $"Добавлено: {imported.Count}" + (failures.Count > 0 ? " · Ошибки: " + string.Join("; ", failures) : " · Выберите материал для работы");
        }
        finally { _workspaceImporting = false; _workspaceBrowser.IsEnabled = true; UpdateBusyIndicator(); }
    }

    private async Task PromptWorkspaceFilesAsync()
    {
        var dialog = new OpenFileDialog { Title = "Добавить материалы", Multiselect = true, Filter = "Материалы Tessera|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ase;*.aseprite;*.tboard;*.ttiles" };
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
                if (asset.SelectedResult is { } result) source.SelectedGeneration = source.Generations.FirstOrDefault(g => g.Id == result) ?? source.SelectedGeneration;
                SelectSource(source);
                if (source.SelectedGeneration is { } selected) SelectGeneration(selected);
                if (asset.Draft is { } draft) RestoreSettings(draft);
                operationSourceInput.SelectedIndex = asset.Basis == 1 && _downscaledResult is not null ? 1 : 0;
                zoomInput.SelectedIndex = Math.Clamp(asset.Zoom, 0, zoomInput.Items.Count - 1);
                backgroundInput.SelectedIndex = Math.Clamp(asset.Background, 0, 2);
                OpenDockedWorkspaceTool("Размер", sizeToolGroup, frameToolGroup, pixelToolGroup);
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
            _workspaceCatalog.State.SelectedAsset = asset.Id; _workspaceCatalog.Save();
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
            string? name = _workspaceBrowser!.Prompt("Создать доску", "Название доски", "Новая доска"); if (string.IsNullOrWhiteSpace(name)) return;
            if (moodboardWorkspace.HasBoard && !moodboardWorkspace.CanLeave()) return;
            var board = new WorkspaceAsset { Kind = WorkspaceAssetKind.Board, Name = name, ContentFile = "content.tboard" };
            string path = _workspaceCatalog.ContentPath(board); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ShowMoodboardWorkspace(); moodboardWorkspace.CreateWorkspaceBoard(1024, 768); await moodboardWorkspace.SaveToFileAsync(path);
            _workspaceCatalog.State.Assets.Add(board); AddNewWorkspaceAsset(board); _workspaceActive = board; moodboardWorkspace.WorkspaceSavePath = path;
            UpdateDocumentPreview(board); return;
        }
        if (action == "new-tiles")
        {
            string? name = _workspaceBrowser!.Prompt("Создать коллекцию тайлов", "Название коллекции", "Новые тайлы"); if (string.IsNullOrWhiteSpace(name)) return;
            var tiles = new WorkspaceAsset { Kind = WorkspaceAssetKind.Tiles, Name = name, ContentFile = "content.ttiles" };
            string path = _workspaceCatalog.ContentPath(tiles); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            TileCollectionFile.Save(path, [], new()); _workspaceCatalog.State.Assets.Add(tiles); AddNewWorkspaceAsset(tiles); await OpenWorkspaceTilesAsync(tiles); return;
        }
        if (asset is null || asset.Kind != WorkspaceAssetKind.Image) return;
        if (_activeSource?.Id != asset.Id) await OpenWorkspaceAssetAsync(asset);
        if (_activeSource is null || _sourceImage is null) return;
        if (action == "background")
        {
            var targetSource = _activeSource;
            ShowBackgroundWorkspace(); backgroundWorkspace.SetImage(WorkspaceBasis(), asset.Name, WorkspaceBasisPath());
            backgroundWorkspace.EnableWorkspaceApply(async image =>
            {
                using (image) SaveWorkspaceImageResult(image, "Удаление фона", targetSource);
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
        _workspaceCatalog.Save(); _workspaceBrowser!.Refresh(asset.Id);
    }

    private Bitmap WorkspaceBasis() => operationSourceInput.SelectedIndex == 1 && _downscaledResult is not null ? _downscaledResult : _sourceImage!;
    private string WorkspaceBasisPath() => operationSourceInput.SelectedIndex == 1 && _activeSource!.SelectedGeneration is { } selected ? _library.ResultPath(_activeSource, selected) : _library.SourcePath(_activeSource!);
    private void SaveWorkspaceImageResult(Bitmap result, string operation, SourceEntry? target = null)
    {
        target ??= _activeSource; if (target is null || !_sources.Contains(target)) throw new InvalidOperationException("Исходный ассет больше не доступен.");
        var entry = _library.SaveGeneration(target, result, null, new GenerationEntry { Operation = operation, Caption = operation, ParentGenerationId = target.SelectedGeneration?.Id, PreservesTransparency = true, Settings = target == _activeSource ? CaptureSettings() : target.DraftSettings ?? new() });
        target.Generations.Add(entry); target.SelectedGeneration = entry;
        if (target == _activeSource) { SelectGeneration(entry); operationSourceInput.SelectedIndex = 1; }
        SynchronizeWorkspace();
    }
    private void ExportWorkspaceImage()
    {
        if (_sourceImage is null) return;
        var dialog = new SaveFileDialog { Title = "Экспортировать изображение", Filter = "PNG с прозрачностью|*.png", DefaultExt = ".png", FileName = Path.GetFileNameWithoutExtension(_workspaceActive?.Name ?? "image") + ".png" };
        if (dialog.ShowDialog(this) != true) return;
        string path = Path.GetFullPath(dialog.FileName);
        if (_sources.Any(s => string.Equals(Path.GetFullPath(s.OriginalPath), path, StringComparison.OrdinalIgnoreCase)) || path.StartsWith(_library.DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Выберите файл вне библиотеки и исходников.");
        WorkspaceBasis().Save(path, System.Drawing.Imaging.ImageFormat.Png); statusLabel.Text = "Экспортировано: " + path;
    }

    private async Task OpenWorkspaceTilesAsync(WorkspaceAsset asset)
    {
        if (_tileInspector is not null) { _tileInspector.Activate(); statusLabel.Text = "Закройте текущую проверку тайлов перед открытием другого материала."; return; }
        var inspector = new TileInspectorWindow { Owner = this }; _tileInspector = inspector;
        inspector.Closed += (_, _) => { _tileInspector = null; SynchronizeWorkspace(); };
        if (asset.Kind == WorkspaceAssetKind.Tiles)
        {
            string path = _workspaceCatalog!.ContentPath(asset); inspector.LoadCollectionFromFile(path); inspector.WorkspaceSavePath = path;
            inspector.WorkspaceSaved += () => { asset.ModifiedAt = DateTimeOffset.Now; UpdateDocumentPreview(asset); _workspaceCatalog.Save(); };
        }
        else
        {
            var targetSource = _activeSource;
            await inspector.LoadFileAsync(WorkspaceBasisPath());
            inspector.EnableWorkspaceApply(tile =>
            {
                using var memory = new MemoryStream(); tile.WritePng(memory); memory.Position = 0;
                using var bitmap = new Bitmap(memory); using var copy = new Bitmap(bitmap);
                SaveWorkspaceImageResult(copy, "Исправление тайла", targetSource); inspector.MarkWorkspaceApplied();
            });
        }
        inspector.Show();
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
            _workspaceCatalog.Save();
            if (_workspaceBrowser?.Visibility == Visibility.Visible) _workspaceBrowser.Refresh();
        }
        catch (Exception ex) { statusLabel.Text = "Каталог: " + ex.Message; }
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
            asset.Basis = operationSourceInput.SelectedIndex; asset.Zoom = zoomInput.SelectedIndex; asset.Background = backgroundInput.SelectedIndex;
            _activeSource.DraftSettings = asset.Draft;
        }
        _workspaceCatalog.Save();
    }

    private void ShowWorkspaceBrowser()
    {
        if (_workspaceBrowser is null) return;
        PersistWorkspaceState(); _workspaceBrowser.Visibility = Visibility.Visible;
        if (_workspaceActions is not null) _workspaceActions.Visibility = Visibility.Collapsed;
        SynchronizeWorkspace(); _workspaceBrowser.Refresh();
        statusLabel.Text = "Библиотека · материалы, проекты и общие результаты";
    }
    private void HideWorkspaceBrowser()
    {
        if (_workspaceBrowser is not null) _workspaceBrowser.Visibility = Visibility.Collapsed;
        if (_workspaceActions is not null) _workspaceActions.Visibility = workspaceHost.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AdoptWorkspaceSource(SourceEntry source)
    {
        if (!_workspaceReady || _workspaceActive?.Id == source.Id) return;
        PersistWorkspaceState();
        _workspaceActive = _workspaceCatalog!.State.Assets.FirstOrDefault(a => a.Id == source.Id);
    }

    private void RestoreWorkspaceImageView(SourceEntry source)
    {
        if (!_workspaceReady || _workspaceCatalog?.State.Assets.FirstOrDefault(a => a.Id == source.Id) is not { } asset) return;
        if (source.DraftSettings is { } draft) RestoreSettings(draft);
        operationSourceInput.SelectedIndex = asset.Basis == 1 && _downscaledResult is not null ? 1 : 0;
        zoomInput.SelectedIndex = Math.Clamp(asset.Zoom, 0, zoomInput.Items.Count - 1);
        backgroundInput.SelectedIndex = Math.Clamp(asset.Background, 0, 2);
        if (_workspaceDock?.Visibility != Visibility.Visible) OpenDockedWorkspaceTool("Размер", sizeToolGroup, frameToolGroup, pixelToolGroup);
    }

    private void OpenDockedWorkspaceTool(string title, params GroupBox[] groups)
    {
        if (_workspaceDock is null) return;
        CloseDockedWorkspaceTool();
        var content = new StackPanel { Margin = new Thickness(14), IsEnabled = !_processing };
        content.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        for (int q = 0; q < groups.Length; q++)
        {
            var group = groups[q]; if (!group.Resources.MergedDictionaries.Contains(Resources)) group.Resources.MergedDictionaries.Add(Resources);
            if (group.Parent is Panel parent) parent.Children.Remove(group); _workspaceGroups.Add(group);
            if (q == 0) content.Children.Add(group);
            else content.Children.Add(new Expander { Header = group.Header, Content = group, IsExpanded = false, Margin = new Thickness(0, 8, 0, 0), Style = (Style)FindResource("ToolExpander") });
        }
        _workspaceToolContent = content;
        _workspaceDock.Child = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _workspaceDock.Visibility = Visibility.Visible; UpdateWorkspaceDockLayout();
    }
    private void CloseDockedWorkspaceTool()
    {
        foreach (var group in _workspaceGroups)
        {
            if (group.Parent is Panel panel) panel.Children.Remove(group); else if (group.Parent is Expander expander) expander.Content = null;
            settingsPanel.Children.Add(group);
        }
        _workspaceGroups.Clear();
        if (_workspaceDock is not null) { _workspaceDock.Child = null; _workspaceDock.Visibility = Visibility.Collapsed; }
    }
    private void UpdateWorkspaceDockLayout()
    {
        if (_workspaceDock is null) return;
        bool compact = ActualWidth < 1000;
        _workspaceDock.Width = compact ? double.NaN : 320; _workspaceDock.MaxHeight = compact ? 260 : double.PositiveInfinity;
        Grid.SetColumn(_workspaceDock, compact ? 0 : 1); Grid.SetRow(_workspaceDock, compact ? 0 : 1);
        workspaceGrid.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 320);
    }
}

public static class WorkspacePrototypeLocation
{
    public static string Prepare()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string root = Path.Combine(AppContext.BaseDirectory, "WorkspaceData", "Library");
        string source = Path.Combine(local, "Tessera", "Library");
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
