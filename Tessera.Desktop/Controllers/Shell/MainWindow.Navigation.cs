using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tessera.Desktop;

public partial class MainWindow
{
    private MenuItem fileMenu = null!, viewMenu = null!, moreMenu = null!;

    private MenuItem openMenuItem = null!, saveMenuItem = null!, saveAllMenuItem = null!, saveDocumentMenuItem = null!, exportMenu = null!;

    private MenuItem importAsepriteMenuItem = null!;

    private MenuItem libraryFileMenu = null!, createMenu = null!, unrealMenu = null!;

    private MenuItem unrealCreateTextureMenuItem = null!, unrealSendMenuItem = null!, unrealMapsMenuItem = null!;

    private MenuItem sizeMenuItem = null!, colorMenuItem = null!, smoothingMenuItem = null!, gridMenuItem = null!, infoMenuItem = null!;

    private MenuItem checkUpdatesMenuItem = null!, zoomMenu = null!, backgroundMenu = null!;

    private MenuItem macrosMenuItem = null!;

    private MenuItem _alternativePreviewItem = null!;

    private MenuItem[] _toolMenus = [];

    private readonly Dictionary<string, MenuItem> _toolMenuItems = [];

    private readonly List<MenuItem> _imageCommands = [];

    private readonly List<MenuItem> _zoomChoices = [], _backgroundChoices = [];

    private bool _navigationReady, _navigationCompact;

    private void InitializeNavigation()
    {
        fileMenu = NavigationItem("fileMenu", Localization.Text("FileMenu"));
        var sizeToolsMenu = NavigationItem("sizeToolsMenu", Localization.Text("SizeAndGrid"));
        var colorToolsMenu = NavigationItem("colorToolsMenu", Localization.Text("Color"));
        var filtersToolsMenu = NavigationItem("filtersToolsMenu", Localization.Text("FiltersAndBackground"));
        var materialsToolsMenu = NavigationItem("materialsToolsMenu", Localization.Text("MaterialsAndAtlases"));
        _toolMenus = [sizeToolsMenu, colorToolsMenu, filtersToolsMenu, materialsToolsMenu];
        viewMenu = NavigationItem("viewMenu", Localization.Text("ViewMenu"));
        moreMenu = NavigationItem("moreMenu", Localization.Text("MoreMenu"));
        mainMenu.Items.Add(fileMenu);
        foreach (var menu in _toolMenus) mainMenu.Items.Add(menu);
        mainMenu.Items.Add(viewMenu);
        openMenuItem = AddNavigation(fileMenu, "openMenuItem", Localization.Text("OpenFilesAction"), OpenNavigationFilesAsync, "Ctrl+O");
        importAsepriteMenuItem = AddNavigation(fileMenu, "importAsepriteMenuItem", Localization.Text("ImportAsepriteAction"), OpenAsepriteImportAsync);
        libraryFileMenu = NavigationItem("libraryFileMenu", Localization.Text("FileMenu")); libraryMenu.Items.Add(libraryFileMenu);
        createMenu = NavigationItem("createMenu", Localization.Text("CreateLabel")); libraryFileMenu.Items.Add(createMenu);
        AddNavigation(createMenu, "createProjectMenuItem", Localization.Text("ProjectAction"), () => CreateNavigationMaterialAsync("project"));
        AddNavigation(createMenu, "createBoardMenuItem", Localization.Text("BoardAction"), () => CreateNavigationMaterialAsync("new-board"));
        fileMenu.Items.Add(new Separator());
        saveDocumentMenuItem = AddNavigation(fileMenu, "saveDocumentMenuItem", Localization.Text("SaveDocument"), SaveNavigationDocumentAsync, "Ctrl+S");
        exportMenu = NavigationItem("exportMenu", Localization.Text("Export")); fileMenu.Items.Add(exportMenu);
        saveMenuItem = AddNavigation(exportMenu, "saveMenuItem", Localization.Text("PNGAction"), ExportNavigationAsync);
        saveAllMenuItem = AddNavigation(exportMenu, "saveAllMenuItem", Localization.Text("AllResultsAsPNGAction"), () => { Editor.History.SaveAllImages(this, new()); return Task.CompletedTask; });
        AddNavigation(exportMenu, "exportIcoMenuItem", Localization.Text("ICOAction"), () => NavigationImageActionAsync("ico"));
        macrosMenuItem = AddTool(fileMenu, "macrosMenuItem", Localization.Text("MacrosAction"), "macros");
        unrealMenu = NavigationItem("unrealMenu", "Unreal Engine 5"); libraryFileMenu.Items.Add(unrealMenu);
        AddNavigation(unrealMenu, "unrealProjectMenuItem", Localization.Text("ConnectUnrealProjectAction"), SelectUnrealProjectAsync);
        AddNavigation(unrealMenu, "unrealInstallMenuItem", Localization.Text("InstallPluginIntoProjectAction"), InstallUnrealPluginAsync);
        unrealCreateTextureMenuItem = AddNavigation(unrealMenu, "unrealCreateTextureMenuItem", Localization.Text("CreateTextureInUnrealAction"), CreateUnrealTextureAsync);
        unrealSendMenuItem = AddNavigation(unrealMenu, "unrealSendMenuItem", Localization.Text("SendSelectedTexture"), SendTextureToUnrealAsync);
        unrealMapsMenuItem = AddNavigation(unrealMenu, "unrealMapsMenuItem", Localization.Text("SendMaterialMaps"), SendMapsToUnrealAsync);
        AddNavigation(unrealMenu, "unrealReceiveMenuItem", Localization.Text("ReceiveTexturesFromUnreal"), ReceiveFromUnrealAsync);
        checkUpdatesMenuItem = NavigationItem("checkUpdatesMenuItem", Localization.Text("CheckForUpdates"));

        sizeMenuItem = AddTool(sizeToolsMenu, "sizeMenuItem", Localization.Text("SizeAndFrameAction"), "size");
        gridMenuItem = AddTool(sizeToolsMenu, "gridMenuItem", Localization.Text("GridAction"), "grid");
        AddTool(sizeToolsMenu, "finalistMenuItem", Localization.Text("TCRReduceImageAction"), "finalists");

        colorMenuItem = AddTool(colorToolsMenu, "colorMenuItem", Localization.Text("PaletteAction"), "color");
        smoothingMenuItem = AddTool(colorToolsMenu, "smoothingMenuItem", Localization.Text("SmoothingAction"), "smoothing");
        AddTool(colorToolsMenu, "channelMenuItem", Localization.Text("RGBAChannelsAction"), "channel");
        infoMenuItem = AddTool(colorToolsMenu, "infoMenuItem", Localization.Text("InfoAction"), "info");

        AddTool(filtersToolsMenu, "monochromeMenuItem", Localization.Text("MonochromeAction"), "monochrome");
        AddTool(filtersToolsMenu, "sharpenMenuItem", Localization.Text("SharpenAction"), "sharpen");
        AddTool(filtersToolsMenu, "asciiMenuItem", Localization.Text("AsciiAction"), "ascii");
        AddNavigation(filtersToolsMenu, "removeBackgroundMenuItem", Localization.Text("RemoveBackgroundAction"), () => NavigationImageActionAsync("background"));

        AddNavigation(materialsToolsMenu, "materialMapsMenuItem", Localization.Text("HeightNormalAOMapsAction"), OpenMaterialMapsAsync);
        AddNavigation(materialsToolsMenu, "rebuildMaterialMapsMenuItem", Localization.Text("RebuildMapsFromTheCurrentVersionAction"), RebuildMaterialMapsAsync);
        AddTool(materialsToolsMenu, "inspectTilesMenuItem", Localization.Text("TileAtlasAction"), TileAtlasPage.ToolKey);
        foreach (var menu in _toolMenus) _imageCommands.AddRange(NavigationDescendants(menu));
        _imageCommands.Add(macrosMenuItem);
        _imageCommands.Add((MenuItem)FindName("exportIcoMenuItem"));

        zoomMenu = NavigationItem("zoomMenu", Localization.Text("Scale")); viewMenu.Items.Add(zoomMenu);
        for (int q = 0; q < EditorPreview.ZoomFactors.Length; q++)
        {
            int index = q;
            string label = q == 0 ? Localization.Text("Fit") : (EditorPreview.ZoomFactors[q] * 100).ToString("0.##", CultureInfo.CurrentCulture) + "%";
            var item = AddNavigation(zoomMenu, "zoomChoice" + q, label, () => { Editor.Preview.PreviewZoom = index; return Task.CompletedTask; });
            item.IsCheckable = true; _zoomChoices.Add(item);
        }
        backgroundMenu = NavigationItem("backgroundMenu", Localization.Text("PreviewBackground")); viewMenu.Items.Add(backgroundMenu);
        string[] backgrounds = ["DarkCheckerboard", "DarkBackground", "LightBackground", "LightCheckerboard"];
        for (int q = 0; q < backgrounds.Length; q++)
        {
            int index = q;
            var item = AddNavigation(backgroundMenu, "backgroundChoice" + q, Localization.Text(backgrounds[q]), () => { Editor.Preview.PreviewBackground = index; return Task.CompletedTask; });
            item.IsCheckable = true; _backgroundChoices.Add(item);
        }
        _alternativePreviewItem = AddNavigation(viewMenu, "alternativePreviewMenuItem", Localization.Text("AlternativePreview"), () => { Editor.Preview.AlternativePreview = !Editor.Preview.AlternativePreview; return Task.CompletedTask; });
        _alternativePreviewItem.IsCheckable = true;
        ToolTipService.SetShowOnDisabled(_alternativePreviewItem, true);
        var lensesMenu = NavigationItem("lensesMenu", Localization.Text("Lenses")); viewMenu.Items.Add(lensesMenu);
        foreach (var lens in AppearanceSettings.Lenses)
            AddTool(lensesMenu, lens.Tool + "MenuItem", Localization.Text(lens.TitleKey), lens.Tool);
        Editor.Preview.UpdateLensKeyHints();
        _imageCommands.AddRange(NavigationDescendants(lensesMenu));
        SizeChanged += (_, _) => UpdateNavigationLayout();
        captionButtons.SizeChanged += (_, _) => UpdateNavigationLayout();
        Loaded += (_, _) => { UpdateNavigationLayout(); UpdateNavigationState(); };
        _navigationReady = true;
        UpdateNavigationState();
    }

    private MenuItem NavigationItem(string name, string text)
    {
        var item = new MenuItem { Header = text };
        AutomationProperties.SetName(item, text.Replace("_", ""));
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

    private MenuItem AddTool(MenuItem parent, string name, string text, string key)
    {
        Editor.Dock.RegisterTool(key, text.TrimEnd('…'));
        var item = AddNavigation(parent, name, text, () => { Editor.Dock.OpenRegisteredTool(key); return Task.CompletedTask; });
        item.IsCheckable = true;
        _toolMenuItems[key] = item;
        return item;
    }

    internal void UpdateToolOpenState()
    {
        if (!_navigationReady) return;
        foreach (var (key, item) in _toolMenuItems)
            item.IsChecked = Editor.Dock.IsToolOpen(key);
        Editor.Preview.UpdateGridLens();
        Editor.Preview.UpdateTileHeatmapLens();
    }

    internal void SetToolGesture(string key, string gesture) => _toolMenuItems[key].InputGestureText = gesture;

    internal void SetComparisonHint(string hint) => _alternativePreviewItem.ToolTip = hint;

    internal bool CanSaveImage => saveMenuItem.IsEnabled;

    internal bool LibraryVisible => LibraryPage.homeScroll.Visibility == Visibility.Visible || _workspaceBrowser?.Visibility == Visibility.Visible;

    internal void UpdateNavigationState()
    {
        if (!_navigationReady) return;
        bool busy = WorkspaceBusy;
        bool image = Editor.ImageEditorVisible;
        bool library = LibraryVisible;
        SetVisible(libraryFrame, library);
        bool board = moodboardWorkspace.Visibility == Visibility.Visible && moodboardWorkspace.HasBoard;
        bool animation = asepriteWorkspace.Visibility == Visibility.Visible && asepriteWorkspace.CurrentSheetPath is not null;
        bool background = backgroundWorkspace.Visibility == Visibility.Visible;
        bool ico = iconWorkspace.Visibility == Visibility.Visible;
        bool canExport = image && !Editor.Preview.ContourPreviewVisible || board || animation && asepriteWorkspace.CanExport || background && backgroundWorkspace.CanExport || ico && iconWorkspace.CanExport;
        openMenuItem.IsEnabled = libraryButton.IsEnabled = libraryFileMenu.IsEnabled = !busy;
        importAsepriteMenuItem.IsEnabled = !busy;
        createMenu.IsEnabled = _workspaceReady && !busy;
        createMenu.Visibility = _workspaceReady ? Visibility.Visible : Visibility.Collapsed;
        foreach (var menu in _toolMenus) menu.IsEnabled = !library && image && !busy;
        macrosMenuItem.Visibility = image ? Visibility.Visible : Visibility.Collapsed;
        var menuVisibility = library ? Visibility.Collapsed : Visibility.Visible;
        if (mainMenu.Visibility != menuVisibility)
        {
            CloseNavigationMenus();
            mainMenu.Visibility = menuVisibility;
            UpdateNavigationLayout();
        }
        libraryMenu.Visibility = library ? Visibility.Visible : Visibility.Collapsed;
        bool libraryTexture = _workspaceReady ? SelectedLibraryImage is not null : Editor._activeSource is not null;
        unrealCreateTextureMenuItem.IsEnabled = library && _workspaceReady && libraryTexture && !busy;
        unrealSendMenuItem.IsEnabled = unrealMapsMenuItem.IsEnabled = library && libraryTexture && !busy;
        foreach (var item in _imageCommands) item.IsEnabled = image && !busy;
        ((MenuItem)FindName("rebuildMaterialMapsMenuItem")).Visibility = _workspaceReady ? Visibility.Visible : Visibility.Collapsed;
        viewMenu.IsEnabled = image;
        saveDocumentMenuItem.IsEnabled = !busy && (_workspaceReady || board || animation);
        saveDocumentMenuItem.Header = Localization.Text(_workspaceReady ? "SaveProject" : "SaveDocument");
        exportMenu.IsEnabled = !busy && canExport;
        saveMenuItem.Header = animation && asepriteWorkspace.IncludeMetadata ? Localization.Text("PNGJSONActionHeading") : ico ? Localization.Text("ICOAction") : Localization.Text("PNGAction");
        saveMenuItem.IsEnabled = !busy && (_workspaceReady ? canExport : image && Editor._downscaledResult is not null && !Editor.Preview.ContourPreviewVisible);
        saveMenuItem.InputGestureText = image && !_workspaceReady ? "Ctrl+S" : "";
        saveAllMenuItem.IsEnabled = !busy && image && Editor._activeSource?.Generations.Count > 0;
        saveAllMenuItem.Visibility = image ? Visibility.Visible : Visibility.Collapsed;
        ((MenuItem)FindName("exportIcoMenuItem")).Visibility = image ? Visibility.Visible : Visibility.Collapsed;
        if (image) { fileLabel.Text = Editor._activeSource!.Label; fileLabel.ToolTip = Editor._activeSource.OriginalPath; }
        else if (_workspaceActive is { } asset && _workspaceBrowser?.Visibility != Visibility.Visible)
        { fileLabel.Text = asset.Name; fileLabel.ToolTip = asset.Kind == EWorkspaceAssetKind.Image ? Editor._activeSource?.OriginalPath : _workspaceCatalog?.ContentPath(asset); }
        else { fileLabel.Text = ""; fileLabel.ToolTip = null; }
        Editor.UpdateAtlasView();
        UpdateNavigationChecks();
    }

    internal void UpdateNavigationChecks()
    {
        if (!_navigationReady) return;
        for (int q = 0; q < _zoomChoices.Count; q++) _zoomChoices[q].IsChecked = Editor.Preview.PreviewZoom == q;
        for (int q = 0; q < _backgroundChoices.Count; q++) _backgroundChoices[q].IsChecked = Editor.Preview.PreviewBackground == q;
        _alternativePreviewItem.IsChecked = Editor.Preview.AlternativePreview;
        UpdateToolOpenState();
        zoomMenu.IsEnabled = true;
    }

    private void UpdateNavigationLayout()
    {
        if (!_navigationReady || mainMenu.Visibility != Visibility.Visible || ActualWidth <= 0) return;
        double width = 0;
        foreach (var item in _toolMenus.Prepend(fileMenu).Append(viewMenu))
        {
            if (item.Visibility != Visibility.Visible) continue;
            var label = new TextBlock { Text = item.Header.ToString()!.Replace("_", ""), FontFamily = mainMenu.FontFamily, FontSize = mainMenu.FontSize };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); width += label.DesiredSize.Width + 20;
        }
        bool compact = width + libraryButton.ActualWidth + captionButtons.ActualWidth + 48 > titleBar.ActualWidth;
        if (compact == _navigationCompact) return;
        CloseNavigationMenus();
        foreach (var item in _toolMenus.Append(viewMenu))
        {
            if (compact) { mainMenu.Items.Remove(item); moreMenu.Items.Add(item); }
            else { moreMenu.Items.Remove(item); mainMenu.Items.Add(item); }
        }
        if (compact) mainMenu.Items.Add(moreMenu); else mainMenu.Items.Remove(moreMenu);
        _navigationCompact = compact;
    }

    internal void CloseNavigationMenus()
    {
        if (!_navigationReady) return;
        foreach (var item in _toolMenus) item.IsSubmenuOpen = false;
        foreach (var item in new[] { libraryFileMenu, fileMenu, viewMenu, moreMenu }) item.IsSubmenuOpen = false;
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
        if (!_workspaceReady || !LibraryVisible || WorkspaceBusy) return;
        if (kind == "project") _workspaceBrowser!.CreateProject(); else await WorkspaceActionAsync(kind, null);
    }

    private async Task NavigationImageActionAsync(string action)
    {
        if (!Editor.ImageEditorVisible || WorkspaceBusy) return;
        if (_workspaceReady) await WorkspaceActionAsync(action, _workspaceActive);
        else if (action == "replace") Editor.Info.OpenColorReplace(System.Drawing.Color.Black, true);
        else if (action == "background") { ShowBackgroundWorkspace(); backgroundWorkspace.SetImage(Editor.ProcessingBasis == 1 ? Editor._downscaledResult! : Editor._sourceImage!, Editor._activeSource!.Label); }
        else if (action == "ico") { ShowIconWorkspace(); iconWorkspace.SetImage(Editor.ProcessingBasis == 1 ? Editor._downscaledResult! : Editor._sourceImage!, Editor._activeSource!.Label); }
    }

    private async Task SaveNavigationDocumentAsync()
    {
        foreach (var window in _materialMapWindows.Values.ToArray()) await window.SaveWorkspaceAsync();
        if (moodboardWorkspace.Visibility == Visibility.Visible) await moodboardWorkspace.SaveWorkspaceAsync();
        PersistWorkspaceState();
        if (Editor.ImageEditorVisible && _workspaceActive is { Unreal: not null } linked)
        {
            var result = Editor.ProcessingBasis == 1 ? Editor._activeSource!.SelectedGeneration : null;
            SaveUnrealTexture(linked, Editor.WorkspaceBasisPath(), result?.Id);
        }
        else Notify(Localization.Text("ProjectSaved"), NotificationKind.Success);
    }

    private async Task ExportNavigationAsync()
    {
        if (Editor.ImageEditorVisible) { if (_workspaceReady) ExportWorkspaceImage(); else Editor.SaveImage(this, EventArgs.Empty); }
        else if (moodboardWorkspace.Visibility == Visibility.Visible) await moodboardWorkspace.ExportDialogAsync();
        else if (asepriteWorkspace.Visibility == Visibility.Visible) await asepriteWorkspace.ExportDialogAsync();
        else if (backgroundWorkspace.Visibility == Visibility.Visible) await backgroundWorkspace.ExportDialogAsync();
        else if (iconWorkspace.Visibility == Visibility.Visible) await iconWorkspace.ExportDialogAsync();
    }
}
