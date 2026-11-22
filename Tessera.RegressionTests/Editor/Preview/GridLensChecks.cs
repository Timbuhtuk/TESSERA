using System.Drawing.Imaging;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tessera.Desktop;
using Bitmap = System.Drawing.Bitmap;
using MediaColor = System.Windows.Media.Color;

namespace Tessera.RegressionTests;

internal static class GridLensChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Verify(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(60))) throw new Exception("Grid lens checks timed out");
        if (failure is not null) throw failure;
    }

    private static void Verify()
    {
        using var bitmap = new Bitmap(32, 24, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.Clear(System.Drawing.Color.FromArgb(255, 32, 32, 32));
        using var original = new MemoryStream(); bitmap.Save(original, ImageFormat.Png);
        var preview = new PixelPreview { Image = bitmap, Zoom = 8 };
        var window = new Window { Content = preview, Width = 360, Height = 300, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show(); Pump();
            byte[] plain = Capture(preview);
            preview.GridLens = new PreviewGridLens(8, 6, 0, 0, MediaColor.FromArgb(255, 255, 0, 0));
            byte[] red = Capture(preview);
            Require(!plain.SequenceEqual(red) && CountPixels(red, 360, 300, 0) > 50, "Grid lines are missing from preview rendering");
            preview.GridLens = new PreviewGridLens(8, 6, 3, 2, MediaColor.FromArgb(255, 255, 0, 0));
            byte[] moved = Capture(preview);
            Require(!red.SequenceEqual(moved), "Grid offset did not move the lines");
            preview.GridLens = new PreviewGridLens(8, 6, 3, 2, MediaColor.FromArgb(255, 0, 255, 0));
            byte[] green = Capture(preview);
            Require(!moved.SequenceEqual(green) && CountPixels(green, 360, 300, 1) > 50, "Grid color did not change");
            preview.Zoom = 4; Pump();
            byte[] zoomed = Capture(preview);
            Require(CountPixels(zoomed, 360, 300, 1) > 20, "Grid vanished after zooming");
            preview.GridLens = new PreviewGridLens(1, 1, 0, 0, MediaColor.FromArgb(255, 255, 0, 0));
            byte[] singlePixel = Capture(preview);
            Require(!zoomed.SequenceEqual(singlePixel) && CountPixels(singlePixel, 360, 300, 0) > 50,
                "One-pixel grid did not render");
            preview.GridLens = null;
            byte[] cleared = Capture(preview);
            Require(!singlePixel.SequenceEqual(cleared), "Closing the lens left lines on the preview");

            var grid = new PreviewGridLens(8, 6, 0, 0, MediaColor.FromRgb(255, 0, 0));
            preview.GridLens = grid;
            int solidPixels = CountPixels(Capture(preview), 360, 300, 0);
            preview.GridLens = grid with { Dashed = true };
            int dashedPixels = CountPixels(Capture(preview), 360, 300, 0);
            Require(dashedPixels > 20 && dashedPixels < solidPixels, "Dashed grid did not render gaps between strokes");
            preview.GridLens = grid with { Color = MediaColor.FromArgb(64, 255, 0, 0) };
            byte[] faded = Capture(preview);
            Require(!cleared.SequenceEqual(faded) && CountPixels(faded, 360, 300, 0) == 0,
                "Lower opacity did not fade the grid strokes");
            preview.GridLens = null;
            Require(cleared.SequenceEqual(Capture(preview)), "Changing grid styles left a preview overlay");
            using var current = new MemoryStream(); bitmap.Save(current, ImageFormat.Png);
            Require(original.ToArray().SequenceEqual(current.ToArray()) && ReferenceEquals(preview.Image, bitmap),
                "Preview grid changed source pixels or exported image data");

            var cells = new PreviewTileGrid(8, 6, 0, 0);
            preview.TileHeatmap = new(cells, true, false, 5, 1, 1);
            byte[] seamless = Capture(preview);
            Require(CountPixels(seamless, 360, 300, 1) > 20 && CountPixels(seamless, 360, 300, 0) == 0,
                "The heatmap did not mark matching edges in green");
            using var mismatched = new Bitmap(bitmap);
            mismatched.SetPixel(7, 3, System.Drawing.Color.White);
            mismatched.SetPixel(18, 5, System.Drawing.Color.White);
            preview.Image = mismatched;
            byte[] horizontal = Capture(preview);
            Require(Sample(horizontal, .5, 3.5) == 0xFFFF3232 && Sample(horizontal, 7.5, 3.5) == 0xFFFF3232 &&
                Sample(horizontal, 8.5, 3.5) == 0xFF30D250,
                "The heatmap did not compare the opposite X edges of each individual tile");
            preview.TileHeatmap = new(cells, false, true, 5, 1, 1);
            byte[] vertical = Capture(preview);
            Require(Sample(vertical, .5, 3.5) == 0xFF202020 && Sample(vertical, 18.5, .5) == 0xFFFF3232 &&
                Sample(vertical, 18.5, 5.5) == 0xFFFF3232,
                "The heatmap ignored its axis settings or did not compare the opposite Y edges of the tile");
            preview.TileHeatmap = new(cells with { OffsetX = 3, OffsetY = 2 }, true, false, 5, 1, 1);
            byte[] shiftedHeatmap = Capture(preview);
            Require(Sample(shiftedHeatmap, .5, 3.5) == 0xFF202020 && Sample(shiftedHeatmap, 3.5, 3.5) == 0xFF30D250 &&
                Sample(shiftedHeatmap, 27.5, 3.5) == 0xFF202020 && Sample(shiftedHeatmap, 3.5, 1.5) == 0xFF202020,
                "The heatmap did not shift to the complete cells of the new grid");
            preview.TileHeatmap = new(new(1, 1, 0, 0), true, true, 5, 1, 1);
            Require(Sample(Capture(preview), 7.5, 3.5) == 0xFF30D250, "A one-pixel tile did not match itself");
            preview.Image = bitmap;
            preview.TileHeatmap = null;
            Require(cleared.SequenceEqual(Capture(preview)), "Closing the heatmap left a preview overlay");

            uint Sample(byte[] pixels, double x, double y)
            {
                var surface = (FrameworkElement)typeof(PixelPreview).GetField("_surface",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
                var bounds = (Rect)surface.GetType().GetProperty("ImageBounds")!.GetValue(surface)!;
                var point = surface.TranslatePoint(
                    new System.Windows.Point(bounds.X + x * bounds.Width / bitmap.Width, bounds.Y + y * bounds.Height / bitmap.Height), preview);
                return BitConverter.ToUInt32(pixels, ((int)point.Y * 360 + (int)point.X) * 4);
            }
        }
        finally { preview.Image = null; window.Close(); Pump(); }
        VerifyEditorModes();
        Console.WriteLine("  Lens rendering, keyboard shortcuts, settings and source PNG isolation passed");
    }

    private static void VerifyEditorModes()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "grid-lens-modes", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "source.png");
        using (var image = new Bitmap(16, 16)) image.Save(path, ImageFormat.Png);
        string library = Path.Combine(folder, "library");
        Directory.CreateDirectory(library);
        string settingsPath = Path.Combine(library, "appearance.json");
        File.WriteAllText(settingsPath, """{"LensKeys":{"tileBoundsLens":"F9"}}""");
        var editor = new MainWindow(library)
            { Width = 1100, Height = 780, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            editor.EnableWorkspacePrototype(); editor.Show(); Pump();
            Wait(editor.ImportWorkspaceFilesAsync([path]));
            Wait(editor.OpenWorkspaceAssetAsync(editor.Workspace!.State.Assets.Single()));
            var source = Get<PixelPreview>(editor, "sourcePreview");
            var gridItem = Get<MenuItem>(editor, "gridLensMenuItem");
            var contourItem = Get<MenuItem>(editor, "contoursMenuItem");
            Require(Equals(Get<MenuItem>(editor, "infoMenuItem").Header, "Инфо…"),
                "Russian information tool label still uses Latin letters");
            NavigationTest.Click(editor, "gridLensMenuItem"); Pump();
            Require(source.GridLens.HasValue && gridItem.IsChecked, "Preview grid did not become the active lens");
            NavigationTest.Click(editor, "contoursMenuItem"); Pump();
            Require(source.GridLens is null && !gridItem.IsChecked && contourItem.IsChecked,
                "Opening contours did not turn off the preview grid lens");
            editor.AlternativePreview = true; Pump();
            Require(!contourItem.IsChecked && !gridItem.IsChecked && source.GridLens is null &&
                Get<PixelPreview>(editor, "_comparisonPreview").GridLens is null,
                "Alternative preview left a lens active");
            var notifications = Get<NotificationDisplay>(editor, "notificationDisplay");
            NavigationTest.Click(editor, "gridLensMenuItem"); Pump();
            Require(!gridItem.IsChecked && source.GridLens is null, "The menu opened a grid lens in alternative preview");
            RequireLensWarning();
            editor.AlternativePreview = false;
            NavigationTest.Click(editor, "gridLensMenuItem"); Pump();
            Require(gridItem.IsChecked && source.GridLens.HasValue,
                "Preview grid could not reopen after leaving alternative preview");
            var (width, height, offsetX, offsetY) = GridInputs(Get<GroupBox>(editor, "_gridLensGroup"));
            offsetX.Value = 3; offsetY.Value = 2;
            width.Value = 1; height.Value = 1; Pump();
            Require(width.Minimum == 1 && height.Minimum == 1 && offsetX.Maximum == 0 && offsetY.Maximum == 0 &&
                offsetX.Value == 0 && offsetY.Value == 0 && source.GridLens is { CellWidth: 1, CellHeight: 1, OffsetX: 0, OffsetY: 0 },
                "One-pixel lens steps or offset bounds are incorrect");

            Require(editor.FindControl("tileBoundsLensMenuItem") is null, "The removed boundary lens is still in the menu");
            var heatmap = Get<MenuItem>(editor, "tileHeatmapLensMenuItem");
            var result = Get<PixelPreview>(editor, "resultPreview");
            source.Focusable = true;
            source.Focus();
            var gridVisible = Get<CheckBox>(editor, "_gridLensVisible");
            gridVisible.IsChecked = false;
            Require(Press(source, Key.Space, pump: false) && source.GridLens.HasValue && gridVisible.IsChecked == false,
                "The comparison key did not temporarily show the hidden grid");
            Require(Press(source, Key.Space, up: true, pump: false) && source.GridLens is null && gridItem.IsChecked,
                "Releasing the comparison key did not restore the hidden grid and keep its settings open");
            gridVisible.IsChecked = true;
            Require(Press(source, Key.D1) && contourItem.IsChecked, "1 did not open the contour lens");
            Require(Press(source, Key.D2) && gridItem.IsChecked, "2 did not open the grid lens");
            Require(!Press(source, Key.D3) && !Press(source, Key.F9) && gridItem.IsChecked,
                "A shortcut for the removed lens still responds");
            var opacity = Get<Slider>(editor, "_gridLensOpacity");
            var dashed = Get<CheckBox>(editor, "_gridLensDashed");
            Require(opacity.Minimum == 0 && opacity.Maximum == 100 && opacity.Value == 63 &&
                source.GridLens is { Color.A: 161, Dashed: false }, "Default grid style changed");
            opacity.Value = 50;
            dashed.IsChecked = true;
            Require(source.GridLens is { Color.A: 128, Dashed: true } && source.GridLens == result.GridLens,
                "Grid opacity and line style did not update both previews");
            opacity.Value = 0;
            Require(source.GridLens is null && result.GridLens is null && gridItem.IsChecked,
                "Zero opacity did not hide the lines while keeping the grid tool open");
            opacity.Value = 100;
            dashed.IsChecked = false;
            Require(source.GridLens is { Color.A: 255, Dashed: false } && source.GridLens == result.GridLens,
                "The grid did not switch back to fully opaque solid lines");
            Require(Press(source, Key.D4) && heatmap.IsChecked && !gridItem.IsChecked &&
                source.TileHeatmap == result.TileHeatmap && source.TileHeatmap?.Grid == new PreviewTileGrid(16, 16, 0, 0),
                "4 did not switch to the heatmap on both previews");
            var (heatmapWidth, heatmapHeight, heatmapOffsetX, heatmapOffsetY) = GridInputs(Get<GroupBox>(editor, "_tileHeatmapLensGroup"));
            heatmapWidth.Value = 5; heatmapHeight.Value = 4; heatmapOffsetX.Value = 4; heatmapOffsetY.Value = 3;
            Require(source.TileHeatmap?.Grid == new PreviewTileGrid(5, 4, 4, 3) && source.TileHeatmap == result.TileHeatmap,
                "The heatmap grid inputs did not update both previews");
            heatmapWidth.Value = 1; heatmapHeight.Value = 1;
            Require(source.TileHeatmap?.Grid == new PreviewTileGrid(1, 1, 0, 0), "The heatmap offsets did not clamp to the step");
            Get<Slider>(editor, "_tileHeatmapThreshold").Value = 12;
            Require(source.TileHeatmap is { Threshold: 12 }, "Heatmap threshold did not update the preview");
            var map = source.TileHeatmap;
            Require(Press(source, Key.Space, pump: false) && source.TileHeatmap is null && result.TileHeatmap is null && heatmap.IsChecked,
                "The comparison key did not hide the heatmap without closing its settings");
            Require(Press(source, Key.Space, pump: false) && source.TileHeatmap is null &&
                Press(source, Key.Space, up: true, pump: false) && source.TileHeatmap == map && result.TileHeatmap == map,
                "The repeated keydown or key release did not preserve the momentary heatmap state");
            BindComparison(Key.D2);
            Require(Press(source, Key.D2, pump: false) && source.GridLens.HasValue && !gridItem.IsChecked && source.TileHeatmap is null,
                "The shared comparison binding did not show the closed grid lens without opening its settings");
            Require(Press(source, Key.D2, up: true, pump: false) && source.GridLens is null && source.TileHeatmap == map,
                "Releasing the shared binding did not restore the previous lens");
            BindComparison(Key.D4);
            Require(Press(source, Key.D4, pump: false) && source.TileHeatmap is null && heatmap.IsChecked,
                "The shared comparison binding toggled the heatmap panel");
            typeof(Window).GetMethod("OnDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(editor, [EventArgs.Empty]);
            Require(source.TileHeatmap == map, "Window deactivation did not restore the lens");
            BindComparison(Key.Space);
            Require(Press(source, Key.D4) && !heatmap.IsChecked && source.TileHeatmap is null,
                "Pressing the heatmap shortcut again did not close it");

            NavigationTest.Click(editor, "appearanceButton"); Pump();
            var settings = editor.OwnedWindows.OfType<AppearanceWindow>().Single();
            var heatmapKey = (Button)settings.FindName("tileHeatmapLensKeyInput");
            Require(settings.FindName("tileBoundsLensKeyInput") is null, "Settings still show the removed lens shortcut");
            Require(Equals(heatmapKey.Content, "4") && heatmap.InputGestureText == "4", "The default heatmap key was not shown");
            heatmapKey.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Press(heatmapKey, Key.F7);
            Require(Equals(heatmapKey.Content, "F7") && heatmap.InputGestureText == "F7" &&
                AppearanceSettings.Load(settingsPath).LensKey("tileHeatmapLens") == Key.F7,
                "Rebinding the heatmap key did not update the settings, menu and saved preference");
            settings.Close(); Pump(); source.Focus();
            Require(!Press(source, Key.D4) && Press(source, Key.F7) && heatmap.IsChecked, "The rebound heatmap key did not take effect");
            editor.AlternativePreview = true; Pump();
            notifications.Clear();
            var comparison = Get<PixelPreview>(editor, "comparisonPreview");
            Require(Press(comparison, Key.F7) && !heatmap.IsChecked && source.TileHeatmap is null,
                "The rebound shortcut was not blocked in alternative preview");
            RequireLensWarning();
            editor.AlternativePreview = false;
            NavigationTest.Click(editor, "appearanceButton"); Pump();
            settings = editor.OwnedWindows.OfType<AppearanceWindow>().Single();
            ((Button)settings.FindName("tileHeatmapLensKeyReset")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(heatmap.InputGestureText == "4" && AppearanceSettings.Load(settingsPath).LensKey("tileHeatmapLens") == Key.D4,
                "Reset did not restore the default heatmap key");
            settings.Close(); Pump();

            void BindComparison(Key key) => WindowTestAccess.Invoke(editor, "ApplyComparisonKey", BindingFlags.Instance | BindingFlags.NonPublic, [key]);

            void RequireLensWarning()
            {
                var card = ProgressUiChecks.Cards(notifications).Last();
                var icon = (System.Windows.Shapes.Path)card.FindName("kindIcon");
                Require(card.IsVisible && card.Kind == NotificationKind.Warning &&
                    ((TextBlock)card.FindName("heading")).Text == "Предупреждение" &&
                    icon.Stroke is SolidColorBrush brush && brush.Color == MediaColor.FromRgb(196, 111, 28) &&
                    card.Message!.Contains("альтернативном просмотре") && !editor.OwnedWindows.OfType<TesseraDialog>().Any(),
                    "A blocked lens did not show a non-modal warning");
            }
        }
        finally { editor.Close(); Pump(); }

        static T Get<T>(MainWindow window, string name)
            => (T)(window.FindControl(name) ?? WindowTestAccess.GetField(window, name, BindingFlags.Instance | BindingFlags.NonPublic))!;
    }

    internal static (IntegerInput Width, IntegerInput Height, IntegerInput OffsetX, IntegerInput OffsetY) GridInputs(GroupBox group)
    {
        var content = (StackPanel)group.Content;
        var fields = content.Children.OfType<DockPanel>().SelectMany(row => row.Children.OfType<IntegerInput>()).ToArray();
        Require(fields.Length == 4, "The lens did not expose four grid controls");
        return (fields[0], fields[1], fields[2], fields[3]);
    }

    private static bool Press(FrameworkElement target, Key key, bool up = false, bool pump = true)
    {
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, Environment.TickCount, key)
        {
            RoutedEvent = up ? Keyboard.PreviewKeyUpEvent : Keyboard.PreviewKeyDownEvent
        };
        target.RaiseEvent(e);
        if (pump) Pump();
        return e.Handled;
    }

    private static void Wait(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        Require(task.IsCompleted, "Lens editor setup timed out");
        task.GetAwaiter().GetResult(); Pump();
    }

    private static byte[] Capture(PixelPreview preview)
    {
        preview.UpdateLayout();
        var image = new RenderTargetBitmap(360, 300, 96, 96, PixelFormats.Pbgra32);
        image.Render(preview);
        byte[] pixels = new byte[360 * 300 * 4];
        image.CopyPixels(pixels, 360 * 4, 0);
        return pixels;
    }

    private static int CountPixels(byte[] pixels, int width, int height, int channel)
    {
        int count = 0;
        for (int q = 0; q < width * height * 4; q += 4)
            if (channel == 0 && pixels[q + 2] > 150 && pixels[q + 1] < 100 ||
                channel == 1 && pixels[q + 1] > 150 && pixels[q + 2] < 100) count++;
        return count;
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
