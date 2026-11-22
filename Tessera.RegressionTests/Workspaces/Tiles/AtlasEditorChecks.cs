using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tessera.Desktop;
using Color = System.Drawing.Color;

namespace Tessera.RegressionTests;

internal static class AtlasEditorChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void RunSta()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Require(thread.Join(TimeSpan.FromSeconds(45)), "Atlas editor timed out");
        if (failure is not null) throw new Exception(failure.ToString(), failure);
    }

    private static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "atlas-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string input = Path.Combine(folder, "atlas.png");
        using var bitmap = new Bitmap(67, 34);
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                bitmap.SetPixel(x, y, Color.FromArgb((x + y) % 256, x, y, 170));
        bitmap.Save(input, System.Drawing.Imaging.ImageFormat.Png);
        byte[] original = File.ReadAllBytes(input);
        VerifyLegacyArchive(folder, original);

        string libraryPath = Path.Combine(folder, "library");
        var library = new ImageLibrary(libraryPath);
        var source = library.Import(input, []);
        bitmap.SetPixel(10, 10, Color.DeepPink);
        var parent = library.SaveGeneration(source, bitmap, null, new GenerationEntry { Caption = "Parent", Operation = "Parent" });
        source.Generations.Add(parent);
        var window = new MainWindow(libraryPath) { Width = 1400, Height = 920, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.EnableWorkspacePrototype();
            window.Show();
            var asset = window.Workspace!.State.Assets.Single();
            Await(window.OpenWorkspaceAssetAsync(asset));
            var history = (ListBox)window.FindControl("generationStrip");
            history.SelectedItem = history.Items.OfType<GenerationEntry>().Single(entry => entry.Id == parent.Id);
            var atlasMenu = (MenuItem)window.FindControl("inspectTilesMenuItem");
            atlasMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump();
            Require(window.Editor.FindName("atlasFrame") is Frame { IsVisible: true, Content: TileAtlasPage atlas } &&
                ReferenceEquals(atlas, window.Editor.Atlas), "The atlas did not activate its own page");
            var host = (Grid)window.FindControl("atlasPreviewHost");
            var editor = (ScrollViewer)window.FindControl("workspaceHost");
            var preview = (TilePreview)window.FindControl("atlasRepeatPreview");
            Require(host.IsVisible && editor.IsVisible && window.OwnedWindows.Count == 0 &&
                Equals(atlasMenu.IsChecked, true) && window.FindControl("createTilesMenuItem") is null,
                "Atlas tool did not open docked inside the image editor");
            Require(window.FindControl("materialsToolsMenu") is MenuItem { IsEnabled: true } && history.IsVisible,
                "Opening the atlas hid the editor menus or history");

            var atlasPanel = (StackPanel)Get<GroupBox>(window, "_atlasToolGroup").Content;
            var gridExpander = atlasPanel.Children.OfType<Expander>().Single();
            var gridSettings = (StackPanel)gridExpander.Content;
            Require(!gridExpander.IsExpanded && !gridSettings.IsVisible && Get<TextBlock>(window, "_atlasLayoutStatus").IsVisible,
                "Atlas grid controls should start collapsed with the layout status visible");
            gridExpander.IsExpanded = true;
            Pump();
            Require(gridSettings.Children.OfType<DockPanel>().Count(row => row.Children.OfType<IntegerInput>().Single().IsVisible) == 6 &&
                gridSettings.Children.OfType<Button>().Single().IsVisible,
                "A single expansion should reveal all grid settings and automatic detection");
            gridExpander.IsExpanded = false;

            Get<IntegerInput>(window, "_atlasTileWidth").Value = 32;
            Get<IntegerInput>(window, "_atlasTileHeight").Value = 32;
            foreach (string field in new[] { "_atlasMarginX", "_atlasMarginY", "_atlasGapX" })
                Get<IntegerInput>(window, field).Value = 1;
            var regions = Get<IReadOnlyList<TileSetRegion>>(window, "_atlasRegions");
            Require(regions.Count == 2 && regions[1].X == 34 && regions[1].Y == 1, "Atlas did not honor margins and spacing");
            Call(window, "ShowAtlasCell", regions[1]);
            Pump();
            Require(preview.Source?.PixelWidth == 32 && preview.Source.PixelHeight == 32 &&
                Math.Abs(preview.Width / preview.PixelScale - 96) < .001 && Math.Abs(preview.Height / preview.PixelScale - 96) < .001 &&
                preview.Analysis is not null && preview.HeatmapVisible, "Tile repeat and seam analysis did not follow the active cell");
            VerifyAtlasHeatmapBinding(window, preview);
            VerifyAtlasNeighbors(window, preview, regions);
            VerifyAtlasRepeatViewport(window, preview);

            var paintTools = (ListBox)window.FindControl("atlasPaintTool");
            var paintRail = (StackPanel)window.FindControl("atlasPaintRail");
            var reset = (Button)window.FindControl("atlasRepeatResetButton");
            Require(paintRail.Orientation == Orientation.Vertical &&
                paintTools.Items.Cast<ListBoxItem>().Select(item => item.Tag).SequenceEqual(new[] { "V", "B", "E", "I", "G", "Shift+G" }),
                "The tile editing tool rail lost the previous tools or their shortcuts");
            Require(paintRail.Children.IndexOf(reset) == paintRail.Children.IndexOf((Button)window.FindControl("atlasColorButton")) + 1,
                "The repeat reset must follow the color button");
            window.Width = 900;
            Pump();
            Require(paintRail.Orientation == Orientation.Horizontal && paintTools.IsVisible, "The tool rail did not fit the narrow viewport");
            window.Width = 1400;
            Pump();

            var cell = Get<PixelEditBuffer>(window, "_atlasCell");
            var swatch = (Border)window.FindControl("atlasColorSwatch");
            paintTools.SelectedIndex = 3;
            Call(window, "PaintAtlasCell", new TilePointer(0, 0, 0, 0), true);
            Require(((SolidColorBrush)swatch.Background).Color == cell.GetPixel(0, 0), "The eyedropper did not update the color swatch");
            VerifyAtlasPalette(window, cell.GetPixel(0, 0).A);
            var paintColor = System.Windows.Media.Color.FromArgb(128, 170, 0, 255);
            Require(((SolidColorBrush)swatch.Background).Color == paintColor, "The swatch lost the palette selection or its alpha");

            var neighbor = cell.GetPixel(1, 0);
            Call(window, "SelectPaintTool", Key.G, ModifierKeys.None, null);
            Call(window, "PaintAtlasCell", new TilePointer(0, 0, 0, 0), true);
            Require(paintTools.SelectedIndex == 4 && cell.GetPixel(1, 0) == neighbor, "G did not select a connected fill");
            Get<Button>(window, "_atlasUndo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Call(window, "SelectPaintTool", Key.G, ModifierKeys.Shift, null);
            Call(window, "PaintAtlasCell", new TilePointer(0, 0, 0, 0), true);
            Require(paintTools.SelectedIndex == 5 && cell.GetPixel(1, 0) != neighbor, "Shift+G did not fill the entire tile");
            Get<Button>(window, "_atlasUndo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            paintTools.SelectedIndex = 1;
            Call(window, "PaintAtlasCell", new TilePointer(0, 0, 0, 0), true);
            Call(window, "FinishAtlasStroke");
            var firstEdit = Get<PixelEditBuffer>(window, "_atlasCell").GetPixel(0, 0);
            Call(window, "SetAtlasNeighbor", 0, regions[0]);
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(ReferenceEquals(preview.TileAt(0), preview.Source) && cell.GetPixel(0, 0) == firstEdit && Get<Button>(window, "_atlasApply").IsEnabled,
                "Resetting the 3×3 layout discarded the tile's pending paint edit");
            Get<Button>(window, "_atlasUndo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(!Get<Button>(window, "_atlasApply").IsEnabled, "Undo left a dirty atlas");
            Get<Button>(window, "_atlasRedo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(Get<Button>(window, "_atlasApply").IsEnabled, "Redo did not restore the atlas draft");
            Call(window, "ShowAtlasCell", regions[0]);
            paintTools.SelectedIndex = 2;
            Call(window, "PaintAtlasCell", new TilePointer(2, 3, 0, 0), true);
            Call(window, "FinishAtlasStroke");
            VerifyAtlasCloseGuard(window);

            window.ProcessingBasis = 0;
            Require(!Get<Button>(window, "_atlasApply").IsEnabled, "Result edits leaked into the source draft");
            Require(Get<System.Collections.IDictionary>(window, "_atlasDrafts").Count == 0 &&
                Get<AtlasDraftStore>(window, "_atlasDraftStore").HasDrafts, "Inactive atlas edits were retained in memory.");
            window.ProcessingBasis = 1;
            Require(Get<Button>(window, "_atlasApply").IsEnabled, "Switching basis discarded a pending draft");
            Get<Button>(window, "_atlasApply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            var reloaded = library.Load().Single();
            var edited = reloaded.Generations.Single(entry => entry.Id != parent.Id);
            Require(edited.ParentGenerationId == parent.Id && edited.OperationId == "manual.tile" && edited.Width == 67 && edited.Height == 34,
                "Atlas correction lost full image dimensions or its parent result");
            Require(!Get<Button>(window, "_atlasApply").IsEnabled && window.Workspace.State.Assets.Count == 1 &&
                asset.Kind == EWorkspaceAssetKind.Image, "Atlas correction created a separate asset or left a saved draft dirty");
            using var before = ImageLibrary.ReadBitmap(library.ResultPath(reloaded, parent));
            using var after = ImageLibrary.ReadBitmap(library.ResultPath(reloaded, edited));
            for (int y = 0; y < after.Height; y++)
                for (int x = 0; x < after.Width; x++)
                {
                    var expected = (x, y) switch
                    {
                        (34, 1) => Color.FromArgb(firstEdit.A, firstEdit.R, firstEdit.G, firstEdit.B),
                        (3, 4) => Color.FromArgb(0, 0, 0, 0),
                        _ => before.GetPixel(x, y)
                    };
                    Require(expected.ToArgb() == after.GetPixel(x, y).ToArgb(), $"Unexpected atlas pixel at ({x}, {y})");
                }
            Require(original.SequenceEqual(File.ReadAllBytes(input)), "Atlas operation modified the original file");

            var shot = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            shot.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(shot));
            using (var output = File.Create(Path.Combine(folder, "editor.png"))) encoder.Save(output);
            Call(window, "CloseDockedTool", "atlas", false);
            Pump();
            Require(!host.IsVisible && ((Grid)window.FindControl("previewPair")).IsVisible, "Closing the atlas did not restore normal preview");
            Console.WriteLine("  Atlas editor capture: " + Path.Combine(folder, "editor.png"));
        }
        finally
        {
            Get<System.Collections.IDictionary>(window, "_atlasDrafts").Clear();
            Get<AtlasDraftStore>(window, "_atlasDraftStore").Dispose();
            window.Close();
            Pump();
        }
    }

    private static void VerifyAtlasNeighbors(MainWindow window, TilePreview preview, IReadOnlyList<TileSetRegion> regions)
    {
        var reset = (Button)window.FindControl("atlasRepeatResetButton");
        var initialSeams = preview.Analysis!.Vertical;
        var originalAnchor = preview.Source;
        var dragType = typeof(TileAtlasPage).GetNestedType("AtlasTileDrag", BindingFlags.NonPublic)!;
        var basis = Get<(Guid Source, Guid? Parent)>(window, "_atlasBasis");
        var layout = Get<TileSetLayout>(window, "_atlasLayout");
        Require(Drop(regions[0], slot: 1, commit: false).Effects == DragDropEffects.Copy &&
            ReferenceEquals(preview.TileAt(1), originalAnchor) && !reset.IsEnabled, "A drag-over changed the layout before the drop");
        Require(Drop(regions[0], slot: 1).Effects == DragDropEffects.Copy &&
            !ReferenceEquals(preview.TileAt(1), originalAnchor) && ReferenceEquals(preview.Source, originalAnchor) &&
            !preview.Analysis.Vertical.SequenceEqual(initialSeams), "Dropping a neighbor changed the anchor or left the old seams");
        Require(Drop(regions[0], slot: 8).Effects == DragDropEffects.Copy &&
            ReferenceEquals(preview.TileAt(1), preview.TileAt(8)) &&
            ReferenceEquals(preview.TileAt(4), originalAnchor), "Dropped tiles did not render in their assigned slots");
        Require(reset.IsEnabled && !Get<Button>(window, "_atlasApply").IsEnabled,
            "Neighbor placement should enable layout reset without marking the atlas image edited");

        Call(window, "ShowAtlasCell", regions[0]);
        Require(Enumerable.Range(0, 9).All(slot => ReferenceEquals(preview.TileAt(slot), preview.Source)),
            "The previous tile's layout leaked into a new selection");
        Require(Drop(regions[1], slot: 2).Effects == DragDropEffects.Copy, "The second tile could not keep a separate layout");
        Call(window, "ShowAtlasCell", regions[1]);
        Require(ReferenceEquals(preview.TileAt(1), preview.TileAt(8)) &&
            !ReferenceEquals(preview.TileAt(1), preview.Source) && ReferenceEquals(preview.TileAt(2), preview.Source),
            "Returning to the first tile did not restore its neighbor placement");
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(!reset.IsEnabled && Enumerable.Range(0, 9).All(slot => ReferenceEquals(preview.TileAt(slot), preview.Source)) &&
            preview.Analysis!.Vertical.SequenceEqual(initialSeams), "Reset did not restore all nine copies and the original seam analysis");
        Call(window, "ShowAtlasCell", regions[0]);
        Require(reset.IsEnabled && !ReferenceEquals(preview.TileAt(2), preview.Source), "Reset cleared another tile's layout");
        Drop(regions[0], slot: 2);
        Require(!reset.IsEnabled && Enumerable.Range(0, 9).All(slot => ReferenceEquals(preview.TileAt(slot), preview.Source)),
            "Dropping the active tile did not restore the default repeat");
        Call(window, "ShowAtlasCell", regions[1]);

        DragEventArgs Drop(TileSetRegion origin, int slot, bool commit = true)
        {
            var tile = Activator.CreateInstance(dragType, basis, layout, origin)!;
            var data = new DataObject("Tessera.AtlasTile", tile);
            var point = new System.Windows.Point((slot % 3 + .5) * preview.Width / 3, (slot / 3 + .5) * preview.Height / 3);
            var e = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), Private,
                null, [data, DragDropKeyStates.None, DragDropEffects.Copy, preview, point], null)!;
            e.RoutedEvent = commit ? DragDrop.PreviewDropEvent : DragDrop.PreviewDragOverEvent;
            preview.RaiseEvent(e);
            return e;
        }
    }

    private static void VerifyAtlasRepeatViewport(MainWindow window, TilePreview preview)
    {
        var scroll = (ScrollViewer)window.FindControl("atlasRepeatScroll");
        Pump();
        double fit = preview.PixelScale;
        var atlasPoint = new System.Windows.Point(preview.ActualWidth / (2 * fit), preview.ActualHeight / (2 * fit));
        var pointer = preview.TranslatePoint(new System.Windows.Point(atlasPoint.X * fit, atlasPoint.Y * fit), scroll);
        const int zoomDelta = 960;
        Call(window, "ZoomAtlasRepeat", zoomDelta, pointer, ModifierKeys.None);
        Pump();
        var projected = preview.TranslatePoint(new System.Windows.Point(atlasPoint.X * preview.PixelScale, atlasPoint.Y * preview.PixelScale), scroll);
        Require(preview.PixelScale > fit && scroll.ScrollableWidth > 0 && scroll.ScrollableHeight > 0 &&
            (projected - pointer).Length < 2,
            $"Repeat zoom did not keep the point under the pointer (fit {fit:F2}, scale {preview.PixelScale:F2}, scrollable {scroll.ScrollableWidth:F1}×{scroll.ScrollableHeight:F1}, drift {(projected - pointer).Length:F2})");
        var middleDown = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Middle) { RoutedEvent = Mouse.PreviewMouseDownEvent };
        preview.RaiseEvent(middleDown);
        Require(middleDown.Handled && scroll.IsMouseCaptured, "The zoomed repeat view did not start middle-button panning");
        var middleUp = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Middle) { RoutedEvent = Mouse.PreviewMouseUpEvent };
        scroll.RaiseEvent(middleUp);
        Require(middleUp.Handled && !scroll.IsMouseCaptured, "The repeat view kept capturing the mouse after panning");
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -zoomDelta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        preview.RaiseEvent(wheel);
        Pump();
        Require(wheel.Handled && Math.Abs(preview.PixelScale - fit) < .001, "The repeat view did not zoom back out with the mouse wheel");
    }

    private static void VerifyAtlasHeatmapBinding(MainWindow window, TilePreview preview)
    {
        var toggle = Get<CheckBox>(window, "_atlasHeatmap");
        var tools = (ListBox)window.FindControl("atlasPaintTool");
        preview.Focus();
        foreach (bool enabled in new[] { true, false })
        {
            toggle.IsChecked = enabled;
            Require(Press(Key.Space).Handled && preview.HeatmapVisible != enabled && toggle.IsChecked == enabled,
                "Holding the comparison key did not invert only the visible heatmap");
            Require(Press(Key.Space).Handled && preview.HeatmapVisible != enabled, "A repeated keydown flipped the heatmap again");
            Require(Press(Key.Space, up: true).Handled && preview.HeatmapVisible == enabled,
                "Releasing the comparison key did not restore the heatmap");
        }

        toggle.IsChecked = true;
        Press(Key.Space);
        Call(window, "ApplyComparisonKey", Key.B);
        Require(preview.HeatmapVisible && !Press(Key.Space).Handled, "Rebinding left the previous key or inversion active");
        Require(Press(Key.B).Handled && !preview.HeatmapVisible && tools.SelectedIndex == 0,
            "The rebound comparison key conflicted with the pencil shortcut");
        Press(Key.B, up: true);
        Require(preview.HeatmapVisible, "Releasing the rebound key did not restore the heatmap");
        Call(window, "ApplyComparisonKey", Key.Space);

        KeyEventArgs Press(Key key, bool up = false)
        {
            var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, key)
            {
                RoutedEvent = up ? Keyboard.PreviewKeyUpEvent : Keyboard.PreviewKeyDownEvent
            };
            preview.RaiseEvent(e);
            return e;
        }
    }

    private static void VerifyAtlasPalette(MainWindow window, byte sampledAlpha)
    {
        bool paletteWorks = false;
        var choose = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        choose.Tick += (_, _) =>
        {
            if (window.OwnedWindows.OfType<ColorReplaceWindow>().SingleOrDefault() is not { } dialog) return;
            choose.Stop();
            var palette = (ListBox)dialog.FindName("paletteList");
            var alpha = (Slider)dialog.FindName("paintAlphaInput");
            var hex = (TextBox)dialog.FindName("newHexInput");
            double initialAlpha = alpha.Value;
            palette.SelectedIndex = 0;
            paletteWorks = palette.IsVisible && alpha.IsVisible && initialAlpha == sampledAlpha &&
                palette.SelectedItem is PaletteColorEntry entry && hex.Text == entry.Hex;
            hex.Text = "#AA00FF";
            alpha.Value = 128;
            ((Button)dialog.FindName("replaceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        choose.Start();
        try { ((Button)window.FindControl("atlasColorButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        finally { choose.Stop(); }
        Require(paletteWorks, "The color square did not open the atlas palette with the sampled alpha");
    }

    private static void VerifyAtlasCloseGuard(MainWindow window)
    {
        var dismiss = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        dismiss.Tick += (_, _) =>
        {
            if (window.OwnedWindows.OfType<TesseraDialog>().FirstOrDefault() is not { } dialog) return;
            dismiss.Stop();
            var buttons = (StackPanel)dialog.FindName("dialogButtons");
            buttons.Children.OfType<Button>().Single(button => Equals(button.Tag, MessageBoxResult.No))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        dismiss.Start();
        try
        {
            window.Close();
            Require(window.IsVisible && Get<Button>(window, "_atlasApply").IsEnabled, "Canceling close discarded atlas edits");
        }
        finally { dismiss.Stop(); }
    }

    private static void VerifyLegacyArchive(string folder, byte[] png)
    {
        string path = Path.Combine(folder, "legacy.ttiles");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var manifest = new StreamWriter(archive.CreateEntry("collection.json").Open()))
                manifest.Write("""{"Version":1,"Settings":{},"Assets":[{"Name":"../Saved tile","Image":"images/00000.png"}]}""");
            using var pixels = archive.CreateEntry("images/00000.png").Open();
            pixels.Write(png);
        }
        byte[] original = File.ReadAllBytes(path);
        string destination = Path.Combine(folder, "recovered");
        Require(LegacyTileArchive.Extract(path, destination) == 1 &&
            File.ReadAllBytes(Directory.GetFiles(destination, "*.png").Single()).SequenceEqual(png) &&
            File.ReadAllBytes(path).SequenceEqual(original), "Legacy extraction lost pixels or changed the archive");
        var catalog = new WorkspaceCatalog(Path.Combine(folder, "legacy-library"));
        catalog.State.Assets.Add(new WorkspaceAsset { Kind = (EWorkspaceAssetKind)3, ContentFile = "content.ttiles" });
        catalog.Save();
        Require(new WorkspaceCatalog(catalog.Root).State.Assets.Single().IsLegacyTileArchive &&
            Enum.GetValues<EWorkspaceAssetKind>().Length == 3, "The catalog cannot read legacy archives or still exposes a Tiles kind");
    }

    private static T Get<T>(MainWindow window, string name) => (T)WindowTestAccess.GetField(window, name, Private)!;
    private static void Call(MainWindow window, string name, params object?[] args) => WindowTestAccess.Invoke(window, name, Private, args);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Await(Task task) { while (!task.IsCompleted) { Pump(); Thread.Sleep(5); } task.GetAwaiter().GetResult(); Pump(); }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
