using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tessera.Desktop;
using Tessera.WpfStyles.Themes;
using Localization = Tessera.Desktop.Localization;

namespace Tessera.RegressionTests;

internal static class NavigationChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(90))) throw new Exception("Navigation verification timed out");
        if (failure is not null) throw failure;
    }
    private static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string root = Path.GetFullPath(Path.Combine("artifacts", "navigation-verification", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        VerifyIndependentEditors(root);
        foreach (var language in new[] { "ru", "en" })
        foreach (var theme in new[] { TesseraTheme.Dark, TesseraTheme.Light })
        {
            string folder = Path.Combine(root, language + "-" + theme); Directory.CreateDirectory(folder);
            new AppearanceSettings { Theme = theme, Language = language }.Save(Path.Combine(folder, "appearance.json"));
            string path = Path.Combine(folder, "source.png");
            using (var bitmap = new Bitmap(24, 16)) { using var draw = Graphics.FromImage(bitmap); draw.Clear(System.Drawing.Color.Coral); bitmap.Save(path); }
            var library = new ImageLibrary(folder); var source = library.Import(path, []);
            using (var bitmap = new Bitmap(8, 4)) source.Generations.Add(library.SaveGeneration(source, bitmap, null, new GenerationEntry()));
            var catalog = new WorkspaceCatalog(folder); catalog.SyncImages([source]);
            catalog.State.Assets.Single().SelectedResult = source.Generations[0].Id;
            var project = catalog.CreateProject("Проект A"); var second = catalog.CreateProject("Проект B");
            var leaf = catalog.CreateFolder(project.Id, null, "Папка");
            catalog.Link(source.Id, project.Id, leaf.Id); catalog.Link(source.Id, second.Id, null);
            var window = new MainWindow(folder) { Width = 1220, Height = 880, ShowActivated = false };
            try
            {
                window.EnableWorkspacePrototype(); window.Show(); Pump();
                var libraryFrame = Get<Frame>("libraryFrame");
                var editorFrame = Get<Frame>("editorFrame");
                Require(libraryFrame.Content == window.LibraryPage && libraryFrame.IsVisible &&
                    editorFrame.Content == window.Editor && !editorFrame.IsVisible, "Startup did not show the library page");
                var menu = Get<Menu>("mainMenu");
                var libraryMenu = Get<Menu>("libraryMenu");
                var libraryFile = Get<MenuItem>("libraryFileMenu");
                var titleBar = Get<Border>("titleBar");
                var file = Get<MenuItem>("fileMenu");
                var view = Get<MenuItem>("viewMenu");
                var more = Get<MenuItem>("moreMenu");
                var groups = new[] { "sizeToolsMenu", "colorToolsMenu", "filtersToolsMenu", "materialsToolsMenu" }.Select(Get<MenuItem>).ToArray();
                MenuItem[] overflow = [.. groups, view];
                MenuItem[] fullMenu = [file, .. overflow];
                Require(menu.Visibility == Visibility.Collapsed && menu.DesiredSize.Width == 0 && libraryMenu.IsVisible &&
                    libraryFile.Items.OfType<MenuItem>().SequenceEqual([Get<MenuItem>("createMenu"), Get<MenuItem>("unrealMenu")]) &&
                    titleBar.ActualHeight - titleBar.Padding.Top == 40, "Library and editor commands are not in separate menus");
                Require(Get<MenuItem>("fileMenu").Header.ToString() == (language == "en" ? "_File" : "_Файл"), "Navigation language is wrong");
                Require(window.FindControl("imageMenu") is null && groups.All(group => !group.IsEnabled) &&
                    Get<MenuItem>("saveDocumentMenuItem").IsEnabled, "Library must allow workspace saving without exposing image tools");
                foreach (string removed in new[] { "editorToolbar", "toolNavigation", "compactToolNavigation", "operationSourceInput", "zoomInput", "backgroundInput", "fileMenuPopup", "toolsMenu", "openToolsMenu" })
                    Require(window.FindControl(removed) is null, "Old navigation control remains: " + removed);
                window.Browser!.Navigate("project", project.Id, leaf.Id);
                window.OpenWorkspaceAssetAsync(window.Workspace!.State.Assets.Single(a => a.Id == source.Id)).GetAwaiter().GetResult(); Pump();
                Require(editorFrame.IsVisible && !libraryFrame.IsVisible && window.Editor.FindName("sourcePreview") is PixelPreview &&
                    window.FindName("sourcePreview") is null, "The image editor did not activate in its own namescope");
                Require(groups.All(group => group.IsVisible && group.IsEnabled) &&
                    Get<MenuItem>("saveMenuItem").IsEnabled && Get<TextBlock>("fileLabel").Text == source.Label, "Tools navigation state is incomplete");
                Require(!libraryMenu.IsVisible && file.Items.OfType<MenuItem>().SequenceEqual([
                    Get<MenuItem>("openMenuItem"), Get<MenuItem>("importAsepriteMenuItem"), Get<MenuItem>("saveDocumentMenuItem"), Get<MenuItem>("exportMenu"), Get<MenuItem>("macrosMenuItem")]),
                    "The editor File menu has the wrong commands");
                string[] groupHeaders = language == "en" ? ["Size and grid", "Color", "Filters and background", "Materials and atlases"]
                    : ["Размер и сетка", "Цвет", "Фильтры и фон", "Материалы и атласы"];
                Require(menu.Items.OfType<MenuItem>().SequenceEqual(fullMenu) &&
                    groups.Select(group => group.Header.ToString()).SequenceEqual(groupHeaders),
                    "Tools categories are incomplete or out of order");
                foreach (var (group, commands) in new[]
                {
                    ("sizeToolsMenu", new[] { "sizeMenuItem", "gridMenuItem", "finalistMenuItem" }),
                    ("colorToolsMenu", new[] { "colorMenuItem", "smoothingMenuItem", "channelMenuItem", "infoMenuItem" }),
                    ("filtersToolsMenu", new[] { "monochromeMenuItem", "sharpenMenuItem", "asciiMenuItem", "removeBackgroundMenuItem" }),
                    ("materialsToolsMenu", new[] { "materialMapsMenuItem", "rebuildMaterialMapsMenuItem", "inspectTilesMenuItem" })
                })
                    Require(Get<MenuItem>(group).Items.Count == commands.Length &&
                        commands.All(name => Get<MenuItem>(group).Items.Contains(Get<MenuItem>(name))), "Tools category lost a command: " + group);
                Require(Get<MenuItem>("viewMenu").Items.Contains(Get<MenuItem>("lensesMenu")) &&
                    Get<MenuItem>("lensesMenu").Items.OfType<MenuItem>()
                        .SequenceEqual(AppearanceSettings.Lenses.Select(lens => Get<MenuItem>(lens.Tool + "MenuItem"))),
                    "View lenses are incomplete or out of order");
                Require(window.FindControl("processingMenu") is null && window.FindControl("profileMenuItem") is null &&
                    window.FindControl("replaceColorMenuItem") is null, "Removed tools remain in navigation");
                Require(window.FindControl("basisMenu") is null, "Processing source still has a second menu selection");
                int historyCount = source.Generations.Count;
                foreach (string command in new[] { "colorMenuItem", "smoothingMenuItem", "contoursMenuItem", "gridLensMenuItem", "channelMenuItem", "monochromeMenuItem", "sharpenMenuItem", "asciiMenuItem", "finalistMenuItem", "gridMenuItem", "infoMenuItem" })
                {
                    NavigationTest.Click(window, command); Pump();
                    Require(Get<MenuItem>(command).IsChecked && Get<Bitmap>("_sourceImage") is not null,
                        "Tool did not remain open");
                }
                var lens = Get<PixelPreview>("sourcePreview").GridLens;
                Require(lens is { CellWidth: 16, CellHeight: 16, OffsetX: 0, OffsetY: 0 } &&
                    Get<PixelPreview>("resultPreview").GridLens == lens, "Grid lens did not cover both previews");
                var (cellWidth, cellHeight, offsetX, offsetY) = GridLensChecks.GridInputs(Get<GroupBox>("_gridLensGroup"));
                cellWidth.Value = 12;
                cellHeight.Value = 7;
                offsetX.Value = 3;
                offsetY.Value = 2;
                lens = Get<PixelPreview>("sourcePreview").GridLens;
                Require(lens is { CellWidth: 12, CellHeight: 7, OffsetX: 3, OffsetY: 2 }, "Grid lens settings did not update");
                window.AlternativePreview = true; Pump();
                Require(Get<PixelPreview>("comparisonPreview").GridLens is null &&
                    Get<PixelPreview>("sourcePreview").GridLens is null &&
                    !Get<MenuItem>("gridLensMenuItem").IsChecked,
                    "Alternative preview kept a lens open");
                window.AlternativePreview = false;
                Get<CheckBox>("_gridLensVisible").IsChecked = false;
                Require(Get<PixelPreview>("sourcePreview").GridLens is null, "Hidden grid lens still draws");
                Get<CheckBox>("_gridLensVisible").IsChecked = true;
                Require(library.Load().Single().Generations.Count == historyCount, "Choosing tools started processing");
                var current = Get<SourceEntry>("_activeSource");
                Get<ListBox>("generationStrip").SelectedItem = current.Generations.Last(); Pump();
                Require(window.ProcessingBasis == 1 && Get<ListBox>("sourceStrip").SelectedItem is null &&
                    Get<ListBox>("generationStrip").SelectedItem == current.Generations.Last(), "Result selection did not become the sole processing source");
                Get<ListBox>("generationStrip").SelectedItem = Get<ListBox>("generationStrip").Items.OfType<HistorySourceRoot>().Single(); Pump();
                Require(window.ProcessingBasis == 0 && Get<ListBox>("sourceStrip").SelectedItem == current &&
                    Get<ListBox>("generationStrip").SelectedItem is HistorySourceRoot historyRoot && historyRoot.Source == current,
                    "Source selection did not replace the result selection");
                NavigationTest.Click(window, "zoomChoice7"); NavigationTest.Click(window, "backgroundChoice2"); Pump();
                Require(window.PreviewZoom == 7 && window.PreviewBackground == 2 && Get<MenuItem>("zoomChoice7").IsChecked && Get<MenuItem>("backgroundChoice2").IsChecked, "View choices did not update/check themselves");
                file.IsSubmenuOpen = true; Pump();
                var popup = (Popup)file.Template.FindName("PART_Popup", file);
                Require(popup.IsOpen && ((Border)popup.Child).Background is SolidColorBrush fill && fill.Color == ((SolidColorBrush)window.FindResource("Ts.Raised")).Color, "Menu did not use the current theme");
                file.IsSubmenuOpen = false;
                NavigationTest.Click(window, "sizeMenuItem"); Pump(); Capture("1220");
                foreach (int width in new[] { 560, 900, 1220 })
                {
                    window.Width = width; Pump();
                    bool compact = menu.Items.Contains(more);
                    Require((width != 560 || compact) && (width != 1220 || !compact) &&
                        menu.Items.OfType<MenuItem>().SequenceEqual(compact ? [file, more] : fullMenu), "Wrong overflow layout at " + width);
                    Require(more.Items.OfType<MenuItem>().SequenceEqual(compact ? overflow : []), "Overflow duplicated or lost sections");
                    var right = menu.TranslatePoint(new System.Windows.Point(menu.ActualWidth, 0), window);
                    var caption = Get<StackPanel>("captionButtons").TranslatePoint(new System.Windows.Point(), window);
                    Require(right.X <= caption.X - 48, "Navigation overlaps the window controls/drag area");
                    if (width == 560) Require(Get<TextBlock>("fileLabel").ActualWidth >= 40,
                        "Minimum-width title bar does not reserve space for the active filename");
                    if (compact)
                    {
                        NavigationTest.Click(window, "moreMenu"); Pump();
                        Get<MenuItem>("sizeToolsMenu").IsSubmenuOpen = true; Pump();
                        Require(more.IsSubmenuOpen && Get<MenuItem>("sizeToolsMenu").IsSubmenuOpen &&
                            Get<MenuItem>("sizeMenuItem").IsEnabled, "Nested overflow lost the size commands");
                        NavigationTest.Click(window, "sizeMenuItem"); Pump();
                        Require(!more.IsSubmenuOpen, "Selecting a tool left overflow open");
                    }
                    Capture(width.ToString());
                }
                window.Width = 560;
                double normalFontSize = menu.FontSize;
                menu.FontSize = 28; Pump();
                WindowTestAccess.Invoke(window, "UpdateNavigationLayout", BindingFlags.Instance | BindingFlags.NonPublic, null); Pump();
                Require(menu.Items.OfType<MenuItem>().SequenceEqual([file, more]) &&
                    more.Items.OfType<MenuItem>().SequenceEqual(overflow), "Large text lost nested overflow");
                NavigationTest.Click(window, "moreMenu"); Pump();
                Get<MenuItem>("colorToolsMenu").IsSubmenuOpen = true; Pump();
                Require(Get<MenuItem>("smoothingMenuItem").IsEnabled, "Color command disabled in nested overflow");
                NavigationTest.Click(window, "smoothingMenuItem"); Pump();
                NavigationTest.Click(window, "libraryButton"); Pump();
                Require(window.Browser.Visibility == Visibility.Visible && window.Workspace.State.ProjectId == project.Id && window.Workspace.State.FolderId == leaf.Id, "Brand navigation lost the project/folder");
                Require(menu.Visibility == Visibility.Collapsed && libraryMenu.IsVisible && !Get<MenuItem>("moreMenu").IsSubmenuOpen,
                    "Returning to the library did not switch from the compact editor menu");
                menu.FontSize = normalFontSize; window.Width = 1220; Pump();
                window.OpenWorkspaceAssetAsync(window.Workspace.State.Assets.Single(a => a.Id == source.Id)).GetAwaiter().GetResult(); Pump();
                Require(menu.Items.OfType<MenuItem>().Where(item => item.IsVisible).SequenceEqual(fullMenu),
                    "Reopening the editor did not restore the full menu after resizing the library");
                Require(window.PreviewZoom == 7 && window.PreviewBackground == 2 && window.ProcessingBasis == 0 &&
                    Get<ListBox>("sourceStrip").SelectedItem == current && Get<ListBox>("generationStrip").SelectedItem is HistorySourceRoot &&
                    Get<ListBox>("sourceStrip").Items.Count == 1, "Navigation lost image settings or project source scope");
                WindowTestAccess.Invoke(window, "SetProcessingState", BindingFlags.Instance | BindingFlags.NonPublic, [true]);
                Require(groups.All(group => !group.IsEnabled) && !Get<MenuItem>("materialMapsMenuItem").IsEnabled &&
                    !Get<MenuItem>("macrosMenuItem").IsEnabled &&
                    !Get<MenuItem>("smoothingMenuItem").IsEnabled && !Get<MenuItem>("gridLensMenuItem").IsEnabled &&
                    !Get<MenuItem>("openMenuItem").IsEnabled && Get<MenuItem>("viewMenu").IsEnabled, "Busy navigation guard is wrong");
                WindowTestAccess.Invoke(window, "SetProcessingState", BindingFlags.Instance | BindingFlags.NonPublic, [false]);
            }
            finally { window.Close(); Pump(); }
            T Get<T>(string name) => (T)(window.FindControl(name) ?? WindowTestAccess.GetField(window, name, BindingFlags.Instance | BindingFlags.NonPublic))!;
            void Capture(string name)
            {
                var content = (FrameworkElement)window.Content;
                var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32); image.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(Path.Combine(folder, "navigation-" + name + ".png")); encoder.Save(stream);
            }
        }
        Localization.Initialize("ru");
        Console.WriteLine("  Navigation artifacts: " + root);
        Console.WriteLine("  RU/EN, dark/light, nested/overflow menus, 560/900/1220 layouts, tool dispatch, checks and state persistence passed");
    }

    private static void VerifyIndependentEditors(string root)
    {
        var first = new MainWindow(Path.Combine(root, "first-editor"));
        var second = new MainWindow(Path.Combine(root, "second-editor"));
        try
        {
            first.PreviewZoom = 5;
            first.PreviewBackground = 2;
            first.AlternativePreview = true;

            Require(second.PreviewZoom == 0 && second.PreviewBackground == 0 && !second.AlternativePreview,
                "An editor inherited preview state from another window");
            Require(!ReferenceEquals(first.Editor.History, second.Editor.History) &&
                !ReferenceEquals(first.Editor.Tools, second.Editor.Tools) && !ReferenceEquals(first.Editor.Dock, second.Editor.Dock),
                "Two editors share a stateful component");
        }
        finally
        {
            first.Close();
            second.Close();
            Pump();
        }
    }
    private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
