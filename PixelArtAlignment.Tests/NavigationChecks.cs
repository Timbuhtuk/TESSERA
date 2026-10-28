using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using Tessera.WpfStyles.Themes;
using Localization = DomainColorTest.Localization;

namespace PixelArtAlignment.Tests;

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
                var menu = Get<Menu>("mainMenu");
                Require(menu.Items.Count == 3 && Get<Border>("titleBar").ActualHeight == 40, "Top navigation is not a single title-bar row");
                Require(Get<MenuItem>("fileMenu").Header.ToString() == (language == "en" ? "_File" : "_Файл"), "Navigation language is wrong");
                Require(window.FindName("imageMenu") is null && !Get<MenuItem>("toolsMenu").IsEnabled &&
                    Get<MenuItem>("saveDocumentMenuItem").IsEnabled, "Library must allow workspace saving without exposing image tools");
                foreach (string removed in new[] { "editorToolbar", "toolNavigation", "compactToolNavigation", "operationSourceInput", "zoomInput", "backgroundInput", "fileMenuPopup" })
                    Require(window.FindName(removed) is null, "Old toolbar control remains: " + removed);
                window.Browser!.Navigate("project", project.Id, leaf.Id);
                window.OpenWorkspaceAssetAsync(window.Workspace!.State.Assets.Single(a => a.Id == source.Id)).GetAwaiter().GetResult(); Pump();
                Require(Get<MenuItem>("toolsMenu").IsEnabled && Get<MenuItem>("saveMenuItem").IsEnabled && Get<TextBlock>("fileLabel").Text == source.Label, "Tools navigation state is incomplete");
                var groups = Get<MenuItem>("toolsMenu").Items.OfType<MenuItem>().ToArray();
                Require(groups.Length == 6 &&
                    groups.Select(group => group.Header.ToString()).SequenceEqual(new[] { "Открытые инструменты", "Размер и сетка", "Цвет", "Фильтры и фон", "Материалы и тайлы", "Макросы…" }.Select(Localization.Text)),
                    "Tools categories are incomplete or out of order");
                foreach (var (group, commands) in new[]
                {
                    ("sizeToolsMenu", new[] { "sizeMenuItem", "gridMenuItem", "finalistMenuItem", "contoursMenuItem" }),
                    ("colorToolsMenu", new[] { "colorMenuItem", "smoothingMenuItem", "channelMenuItem", "infoMenuItem" }),
                    ("filtersToolsMenu", new[] { "filtersMenuItem", "removeBackgroundMenuItem" }),
                    ("materialsToolsMenu", new[] { "materialMapsMenuItem", "inspectTilesMenuItem", "addToBoardMenuItem" })
                })
                    Require(Get<MenuItem>(group).Items.Count == commands.Length &&
                        commands.All(name => Get<MenuItem>(group).Items.Contains(Get<MenuItem>(name))), "Tools category lost a command: " + group);
                Require(window.FindName("processingMenu") is null && window.FindName("profileMenuItem") is null &&
                    window.FindName("replaceColorMenuItem") is null, "Removed tools remain in navigation");
                Require(window.FindName("basisMenu") is null, "Processing source still has a second menu selection");
                int historyCount = source.Generations.Count;
                foreach (string command in new[] { "colorMenuItem", "smoothingMenuItem", "contoursMenuItem", "channelMenuItem", "filtersMenuItem", "finalistMenuItem", "gridMenuItem", "infoMenuItem" })
                {
                    NavigationTest.Click(window, command); Pump();
                    Require(Get<MenuItem>(command).IsChecked && Get<Bitmap>("_sourceImage") is not null,
                        "Tool did not remain open");
                }
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
                Get<MenuItem>("fileMenu").IsSubmenuOpen = true; Pump();
                var file = Get<MenuItem>("fileMenu"); var popup = (Popup)file.Template.FindName("PART_Popup", file);
                Require(popup.IsOpen && ((Border)popup.Child).Background is SolidColorBrush fill && fill.Color == ((SolidColorBrush)window.FindResource("Ts.Raised")).Color, "Menu did not use the current theme");
                file.IsSubmenuOpen = false;
                NavigationTest.Click(window, "sizeMenuItem"); Pump(); Capture("1220");
                foreach (int width in new[] { 560, 900, 1220 })
                {
                    window.Width = width; Pump();
                    bool compact = menu.Items.Count == 2;
                    Require(menu.Items.Count is 2 or 3 && (width == 560 || !compact), "Wrong overflow layout at " + width);
                    Require(Get<MenuItem>("moreMenu").Items.Count == (compact ? 2 : 0), "Overflow duplicated or lost sections");
                    var right = menu.TranslatePoint(new System.Windows.Point(menu.ActualWidth, 0), window);
                    var caption = Get<StackPanel>("captionButtons").TranslatePoint(new System.Windows.Point(), window);
                    Require(right.X <= caption.X - 48, "Navigation overlaps the window controls/drag area");
                    if (compact)
                    {
                        NavigationTest.Click(window, "moreMenu"); Pump();
                        Get<MenuItem>("toolsMenu").IsSubmenuOpen = true; Pump();
                        Get<MenuItem>("sizeToolsMenu").IsSubmenuOpen = true; Pump();
                        Require(Get<MenuItem>("sizeMenuItem").IsEnabled, "Nested overflow lost the size commands");
                        NavigationTest.Click(window, "sizeMenuItem"); Pump();
                    }
                    Capture(width.ToString());
                }
                window.Width = 560;
                double normalFontSize = menu.FontSize;
                menu.FontSize = 28; Pump();
                typeof(MainWindow).GetMethod("UpdateNavigationLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null); Pump();
                Require(menu.Items.Count == 2 && Get<MenuItem>("moreMenu").Items.Count == 2, "Large text lost nested overflow");
                NavigationTest.Click(window, "moreMenu"); Pump();
                Get<MenuItem>("toolsMenu").IsSubmenuOpen = true; Pump();
                Get<MenuItem>("colorToolsMenu").IsSubmenuOpen = true; Pump();
                Require(Get<MenuItem>("smoothingMenuItem").IsEnabled, "Color command disabled in nested overflow");
                NavigationTest.Click(window, "smoothingMenuItem"); Pump();
                menu.FontSize = normalFontSize; window.Width = 1220; Pump();
                NavigationTest.Click(window, "libraryButton"); Pump();
                Require(window.Browser.Visibility == Visibility.Visible && window.Workspace.State.ProjectId == project.Id && window.Workspace.State.FolderId == leaf.Id, "Brand navigation lost the project/folder");
                window.OpenWorkspaceAssetAsync(window.Workspace.State.Assets.Single(a => a.Id == source.Id)).GetAwaiter().GetResult(); Pump();
                Require(window.PreviewZoom == 7 && window.PreviewBackground == 2 && window.ProcessingBasis == 0 &&
                    Get<ListBox>("sourceStrip").SelectedItem == current && Get<ListBox>("generationStrip").SelectedItem is HistorySourceRoot &&
                    Get<ListBox>("sourceStrip").Items.Count == 1, "Navigation lost image settings or project source scope");
                typeof(MainWindow).GetMethod("SetProcessingState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [true]);
                Require(!Get<MenuItem>("toolsMenu").IsEnabled && !Get<MenuItem>("materialMapsMenuItem").IsEnabled && !Get<MenuItem>("smoothingMenuItem").IsEnabled && !Get<MenuItem>("openMenuItem").IsEnabled && Get<MenuItem>("viewMenu").IsEnabled, "Busy navigation guard is wrong");
                typeof(MainWindow).GetMethod("SetProcessingState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [false]);
            }
            finally { window.Close(); Pump(); }
            T Get<T>(string name) => (T)(window.FindName(name) ?? typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))!;
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
    private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
