using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DomainColorTest;

public partial class MainWindow
{
    private MenuItem fileMenu = null!, toolsMenu = null!, viewMenu = null!, moreMenu = null!;
    private MenuItem openMenuItem = null!, saveMenuItem = null!, saveAllMenuItem = null!, saveDocumentMenuItem = null!, exportMenu = null!;
    private MenuItem createMenu = null!, returnMenuItem = null!, resultBasisMenuItem = null!, sourceBasisMenuItem = null!;
    private MenuItem profileMenuItem = null!, sizeMenuItem = null!, colorMenuItem = null!, smoothingMenuItem = null!, gridMenuItem = null!, filtersMenuItem = null!, infoMenuItem = null!;
    private MenuItem checkUpdatesMenuItem = null!, zoomMenu = null!, backgroundMenu = null!;
    private readonly List<MenuItem> _imageCommands = [];
    private readonly List<MenuItem> _zoomChoices = [], _backgroundChoices = [];
    private bool _navigationReady, _navigationCompact;
    private int _processingBasis, _previewZoom, _previewBackground;

    public int ProcessingBasis
    {
        get => _processingBasis;
        set
        {
            int next = value == 1 && _downscaledResult is not null ? 1 : 0;
            if (_processingBasis == next) return;
            _processingBasis = next;
            if (aspectLock.IsChecked == true) SizeChangedByUser(widthChanged: true); else UpdateSizeHint();
            SetProcessingState(_processing);
            ScheduleLiveToolPreviews();
        }
    }
    public int PreviewZoom
    {
        get => _previewZoom;
        set { _previewZoom = Math.Clamp(value, 0, ZoomFactors.Length - 1); UpdatePreviewSettings(); UpdateNavigationChecks(); }
    }
    public int PreviewBackground
    {
        get => _previewBackground;
        set { _previewBackground = Math.Clamp(value, 0, 3); UpdatePreviewSettings(); UpdateNavigationChecks(); }
    }

    private void InitializeNavigation()
    {
        fileMenu = NavigationItem("fileMenu", "_Файл");
        toolsMenu = NavigationItem("toolsMenu", "Инс_трументы");
        viewMenu = NavigationItem("viewMenu", "_Вид");
        moreMenu = NavigationItem("moreMenu", "_Ещё");
        foreach (var menu in new[] { fileMenu, toolsMenu, viewMenu }) mainMenu.Items.Add(menu);
        openMenuItem = AddNavigation(fileMenu, "openMenuItem", "Открыть файлы…", OpenNavigationFilesAsync, "Ctrl+O");
        createMenu = NavigationItem("createMenu", "Создать"); fileMenu.Items.Add(createMenu);
        AddNavigation(createMenu, "createProjectMenuItem", "Проект…", () => CreateNavigationMaterialAsync("project"));
        AddNavigation(createMenu, "createBoardMenuItem", "Доску…", () => CreateNavigationMaterialAsync("new-board"));
        AddNavigation(createMenu, "createTilesMenuItem", "Коллекцию тайлов…", () => CreateNavigationMaterialAsync("new-tiles"));
        fileMenu.Items.Add(new Separator());
        saveDocumentMenuItem = AddNavigation(fileMenu, "saveDocumentMenuItem", "Сохранить документ", SaveNavigationDocumentAsync, "Ctrl+S");
        exportMenu = NavigationItem("exportMenu", "Экспортировать"); fileMenu.Items.Add(exportMenu);
        saveMenuItem = AddNavigation(exportMenu, "saveMenuItem", "PNG…", ExportNavigationAsync);
        saveAllMenuItem = AddNavigation(exportMenu, "saveAllMenuItem", "Все результаты PNG…", () => { SaveAllImages(this, new()); return Task.CompletedTask; });
        AddNavigation(exportMenu, "exportIcoMenuItem", "ICO…", () => NavigationImageActionAsync("ico"));
        fileMenu.Items.Add(new Separator());
        returnMenuItem = AddNavigation(fileMenu, "returnMenuItem", "Вернуться в библиотеку", () => { GoToLibrary(this, new()); return Task.CompletedTask; });
        checkUpdatesMenuItem = NavigationItem("checkUpdatesMenuItem", "Проверить обновления");

        var processingMenu = NavigationItem("processingMenu", "Обработка"); toolsMenu.Items.Add(processingMenu);
        var basisMenu = NavigationItem("basisMenu", "Обрабатывать"); processingMenu.Items.Add(basisMenu);
        sourceBasisMenuItem = AddNavigation(basisMenu, "sourceBasisMenuItem", "Исходник", () => { ProcessingBasis = 0; return Task.CompletedTask; });
        resultBasisMenuItem = AddNavigation(basisMenu, "resultBasisMenuItem", "Выбранный результат", () => { ProcessingBasis = 1; return Task.CompletedTask; });
        sourceBasisMenuItem.IsCheckable = resultBasisMenuItem.IsCheckable = true;
        profileMenuItem = AddTool(processingMenu, "profileMenuItem", "Готовые сценарии…", "profile", 320, profileToolGroup, executionToolGroup);
        AddTool(processingMenu, "macrosMenuItem", "Макросы…", "macros", 460, _macrosGroup);

        var sizeToolsMenu = NavigationItem("sizeToolsMenu", "Размер и сетка"); toolsMenu.Items.Add(sizeToolsMenu);
        sizeMenuItem = AddTool(sizeToolsMenu, "sizeMenuItem", "Размер и кадр…", "size", 370, sizeToolGroup, frameToolGroup, pixelToolGroup);
        gridMenuItem = AddTool(sizeToolsMenu, "gridMenuItem", "Сетка…", "grid", 400, gridToolGroup);

        var colorToolsMenu = NavigationItem("colorToolsMenu", "Цвет"); toolsMenu.Items.Add(colorToolsMenu);
        colorMenuItem = AddTool(colorToolsMenu, "colorMenuItem", "Число цветов…", "color", 440, colorToolGroup);
        smoothingMenuItem = AddTool(colorToolsMenu, "smoothingMenuItem", "Очистить цвета…", "smoothing", 470, smoothingToolGroup);
        AddNavigation(colorToolsMenu, "replaceColorMenuItem", "Заменить цвет…", () => NavigationImageActionAsync("replace"));
        infoMenuItem = AddTool(colorToolsMenu, "infoMenuItem", "Цвета и сведения…", "info", 760, infoToolGroup);

        var filtersToolsMenu = NavigationItem("filtersToolsMenu", "Фильтры и фон"); toolsMenu.Items.Add(filtersToolsMenu);
        filtersMenuItem = AddTool(filtersToolsMenu, "filtersMenuItem", "Фильтры…", "filters", 410, filtersToolGroup);
        AddNavigation(filtersToolsMenu, "removeBackgroundMenuItem", "Удалить фон…", () => NavigationImageActionAsync("background"));

        var materialsToolsMenu = NavigationItem("materialsToolsMenu", "Материалы и тайлы"); toolsMenu.Items.Add(materialsToolsMenu);
        AddNavigation(materialsToolsMenu, "materialMapsMenuItem", "Карты Height / Normal / AO…", OpenMaterialMapsAsync);
        AddNavigation(materialsToolsMenu, "inspectTilesMenuItem", "Проверить тайлы…", () => NavigationImageActionAsync("tiles"));
        AddNavigation(materialsToolsMenu, "addToBoardMenuItem", "Добавить на доску…", () => NavigationImageActionAsync("board"));

        _imageCommands.AddRange(NavigationDescendants(toolsMenu));
        _imageCommands.Add((MenuItem)FindName("exportIcoMenuItem"));

        zoomMenu = NavigationItem("zoomMenu", "Масштаб"); viewMenu.Items.Add(zoomMenu);
        for (int q = 0; q < ZoomFactors.Length; q++)
        {
            int index = q;
            string label = q == 0 ? Localization.Text("Вписать") : (ZoomFactors[q] * 100).ToString("0.##", CultureInfo.CurrentCulture) + "%";
            var item = AddNavigation(zoomMenu, "zoomChoice" + q, label, () => { PreviewZoom = index; return Task.CompletedTask; });
            item.IsCheckable = true; _zoomChoices.Add(item);
        }
        backgroundMenu = NavigationItem("backgroundMenu", "Фон предпросмотра"); viewMenu.Items.Add(backgroundMenu);
        string[] backgrounds = ["Тёмная шахматка", "Тёмный", "Светлый", "Светлая шахматка"];
        for (int q = 0; q < backgrounds.Length; q++)
        {
            int index = q;
            var item = AddNavigation(backgroundMenu, "backgroundChoice" + q, backgrounds[q], () => { PreviewBackground = index; return Task.CompletedTask; });
            item.IsCheckable = true; _backgroundChoices.Add(item);
        }
        _alternativePreviewItem = AddNavigation(viewMenu, "alternativePreviewMenuItem", "Альтернативный просмотр · удерживайте Alt для исходника", () => { AlternativePreview = !AlternativePreview; return Task.CompletedTask; });
        _alternativePreviewItem.IsCheckable = true;
        _alternativePreviewItem.ToolTip = Localization.Text("Показывает выбранный результат во всей области предпросмотра. Удерживайте Alt для просмотра исходника. Если выключить, исходник и результат отображаются в отдельных областях.");
        ToolTipService.SetShowOnDisabled(_alternativePreviewItem, true);
        SizeChanged += (_, _) => UpdateNavigationLayout();
        Loaded += (_, _) => { UpdateNavigationLayout(); UpdateNavigationState(); };
        _navigationReady = true;
        UpdateNavigationState();
    }

    private MenuItem NavigationItem(string name, string text)
    {
        var item = new MenuItem { Header = Localization.Text(text) };
        AutomationProperties.SetName(item, Localization.Text(text).Replace("_", ""));
        item.SubmenuOpened += (_, e) => { if (ReferenceEquals(e.OriginalSource, item)) UpdateNavigationState(); };
        RegisterName(name, item);
        return item;
    }
    private MenuItem AddNavigation(MenuItem parent, string name, string text, Func<Task> action, string gesture = "")
    {
        var item = NavigationItem(name, text); item.InputGestureText = gesture;
        item.Click += async (sender, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, item)) return;
            e.Handled = true;
            CloseNavigationMenus();
            try { await action(); }
            catch (Exception ex) { if (_workspaceReady) WorkspaceError(ex); else TesseraDialog.Show(this, ex.Message, "Tessera", MessageBoxButton.OK, MessageBoxImage.Warning); }
            finally { UpdateNavigationState(); }
        };
        parent.Items.Add(item); return item;
    }
    private static IEnumerable<MenuItem> NavigationDescendants(MenuItem parent)
    {
        foreach (var item in parent.Items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in NavigationDescendants(item)) yield return child;
        }
    }
    private MenuItem AddTool(MenuItem parent, string name, string text, string key, double height, params GroupBox[] groups)
        => AddNavigation(parent, name, text, () => { OpenEditorTool(key, Localization.Text(text).TrimEnd('…'), height, groups); if (key == "info") RefreshInfo(); return Task.CompletedTask; });

    private bool ImageEditorVisible => workspaceHost.Visibility == Visibility.Visible && _sourceImage is not null && _activeSource is not null && (!_workspaceReady || _workspaceActive?.Kind == WorkspaceAssetKind.Image);
    private void UpdateNavigationState()
    {
        if (!_navigationReady) return;
        bool busy = WorkspaceBusy;
        bool image = ImageEditorVisible;
        bool board = moodboardWorkspace.Visibility == Visibility.Visible && moodboardWorkspace.HasBoard;
        bool animation = asepriteWorkspace.Visibility == Visibility.Visible && asepriteWorkspace.CurrentSheetPath is not null;
        bool background = backgroundWorkspace.Visibility == Visibility.Visible;
        bool ico = iconWorkspace.Visibility == Visibility.Visible;
        bool tiles = _workspaceActive?.Kind == WorkspaceAssetKind.Tiles && _tileInspector is not null;
        bool canExport = image || board || tiles || animation && asepriteWorkspace.CanExport || background && backgroundWorkspace.CanExport || ico && iconWorkspace.CanExport;
        openMenuItem.IsEnabled = libraryButton.IsEnabled = returnMenuItem.IsEnabled = !busy;
        createMenu.IsEnabled = _workspaceReady && !busy;
        toolsMenu.IsEnabled = image && !busy;
        foreach (var item in _imageCommands) item.IsEnabled = image && !busy;
        viewMenu.IsEnabled = image;
        saveDocumentMenuItem.IsEnabled = !busy && (_workspaceReady || board || animation || tiles);
        saveDocumentMenuItem.Header = Localization.Text(_workspaceReady ? "Сохранить проект" : "Сохранить документ");
        exportMenu.IsEnabled = !busy && canExport;
        saveMenuItem.Header = animation ? Localization.Text("PNG + JSON…") : ico ? Localization.Text("ICO…") : Localization.Text("PNG…");
        saveMenuItem.IsEnabled = !busy && (_workspaceReady ? canExport : image && _downscaledResult is not null);
        saveMenuItem.InputGestureText = image && !_workspaceReady ? "Ctrl+S" : "";
        saveAllMenuItem.IsEnabled = !busy && image && _activeSource?.Generations.Count > 0;
        saveAllMenuItem.Visibility = image ? Visibility.Visible : Visibility.Collapsed;
        ((MenuItem)FindName("exportIcoMenuItem")).Visibility = image ? Visibility.Visible : Visibility.Collapsed;
        var selected = _activeSource?.SelectedGeneration;
        int number = selected is null ? -1 : _activeSource!.Generations.IndexOf(selected) + 1;
        resultBasisMenuItem.Header = number > 0 ? Localization.Format("Результат №{0}", number) : Localization.Text("Выбранный результат");
        resultBasisMenuItem.IsEnabled = image && !busy && _downscaledResult is not null;
        if (image) { fileLabel.Text = _activeSource!.Label; fileLabel.ToolTip = _activeSource.OriginalPath; }
        else if (_workspaceActive is { } asset && _workspaceBrowser?.Visibility != Visibility.Visible)
        { fileLabel.Text = asset.Name; fileLabel.ToolTip = asset.Kind == WorkspaceAssetKind.Image ? _activeSource?.OriginalPath : _workspaceCatalog?.ContentPath(asset); }
        else { fileLabel.Text = ""; fileLabel.ToolTip = null; }
        UpdateNavigationChecks();
    }
    private void UpdateNavigationChecks()
    {
        if (!_navigationReady) return;
        sourceBasisMenuItem.IsChecked = ProcessingBasis == 0; resultBasisMenuItem.IsChecked = ProcessingBasis == 1;
        for (int q = 0; q < _zoomChoices.Count; q++) _zoomChoices[q].IsChecked = PreviewZoom == q;
        for (int q = 0; q < _backgroundChoices.Count; q++) _backgroundChoices[q].IsChecked = PreviewBackground == q;
        _alternativePreviewItem.IsChecked = AlternativePreview;
        zoomMenu.IsEnabled = true;
    }
    private void UpdateNavigationLayout()
    {
        if (!_navigationReady || ActualWidth <= 0) return;
        double width = 0;
        foreach (var item in new[] { fileMenu, toolsMenu, viewMenu })
        {
            var label = new TextBlock { Text = item.Header.ToString()!.Replace("_", ""), FontFamily = mainMenu.FontFamily, FontSize = mainMenu.FontSize };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); width += label.DesiredSize.Width + 20;
        }
        bool compact = width + libraryButton.ActualWidth + captionButtons.ActualWidth + 48 > titleBar.ActualWidth;
        if (compact == _navigationCompact) return;
        CloseNavigationMenus();
        foreach (var item in new[] { toolsMenu, viewMenu })
        {
            if (compact) { mainMenu.Items.Remove(item); moreMenu.Items.Add(item); }
            else { moreMenu.Items.Remove(item); mainMenu.Items.Add(item); }
        }
        if (compact) mainMenu.Items.Add(moreMenu); else mainMenu.Items.Remove(moreMenu);
        _navigationCompact = compact;
    }
    private void CloseNavigationMenus()
    {
        if (!_navigationReady) return;
        foreach (var item in new[] { fileMenu, toolsMenu, viewMenu, moreMenu }) item.IsSubmenuOpen = false;
    }

    private async Task OpenNavigationFilesAsync()
    {
        if (asepriteWorkspace.Visibility == Visibility.Visible) await asepriteWorkspace.OpenAsync();
        else if (iconWorkspace.Visibility == Visibility.Visible) await iconWorkspace.OpenAsync();
        else if (backgroundWorkspace.Visibility == Visibility.Visible) await backgroundWorkspace.OpenAsync();
        else if (moodboardWorkspace.Visibility == Visibility.Visible) await moodboardWorkspace.AddFilesFromDialogAsync();
        else OpenImage(this, EventArgs.Empty);
    }
    private async Task CreateNavigationMaterialAsync(string kind)
    {
        if (!_workspaceReady || WorkspaceBusy) return;
        ShowHome();
        if (kind == "project") _workspaceBrowser!.CreateProject(); else await WorkspaceActionAsync(kind, null);
    }
    private async Task NavigationImageActionAsync(string action)
    {
        if (!ImageEditorVisible || WorkspaceBusy) return;
        if (_workspaceReady) await WorkspaceActionAsync(action, _workspaceActive);
        else if (action == "replace") OpenColorReplace(System.Drawing.Color.Black, true);
        else if (action == "background") { ShowBackgroundWorkspace(); backgroundWorkspace.SetImage(ProcessingBasis == 1 ? _downscaledResult! : _sourceImage!, _activeSource!.Label); }
        else if (action == "ico") { ShowIconWorkspace(); iconWorkspace.SetImage(ProcessingBasis == 1 ? _downscaledResult! : _sourceImage!, _activeSource!.Label); }
        else if (action == "tiles") OpenTileInspector(this, new());
    }
    private async Task SaveNavigationDocumentAsync()
    {
        foreach (var window in _materialMapWindows.Values.ToArray()) await window.SaveWorkspaceAsync();
        if (moodboardWorkspace.Visibility == Visibility.Visible) await moodboardWorkspace.SaveWorkspaceAsync();
        if (_workspaceActive?.Kind == WorkspaceAssetKind.Tiles && _tileInspector is not null) _tileInspector.SaveWorkspaceDocument();
        PersistWorkspaceState(); statusLabel.Text = Localization.Text("Проект сохранён.");
    }
    private async Task ExportNavigationAsync()
    {
        if (ImageEditorVisible) { if (_workspaceReady) ExportWorkspaceImage(); else SaveImage(this, EventArgs.Empty); }
        else if (moodboardWorkspace.Visibility == Visibility.Visible) await moodboardWorkspace.ExportDialogAsync();
        else if (asepriteWorkspace.Visibility == Visibility.Visible) await asepriteWorkspace.ExportDialogAsync();
        else if (backgroundWorkspace.Visibility == Visibility.Visible) await backgroundWorkspace.ExportDialogAsync();
        else if (iconWorkspace.Visibility == Visibility.Visible) await iconWorkspace.ExportDialogAsync();
        else if (_workspaceActive?.Kind == WorkspaceAssetKind.Tiles && _tileInspector is not null) _tileInspector.ExportDialog();
    }
}
