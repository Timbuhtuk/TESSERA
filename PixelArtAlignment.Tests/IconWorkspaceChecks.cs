using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtDownscale;
using Point = System.Windows.Point;

namespace PixelArtAlignment.Tests;

internal static class IconWorkspaceChecks
{
    [StructLayout(LayoutKind.Sequential)] private struct ScreenPoint { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out ScreenPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    public static void Run()
    {
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "icons-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        var window = new MainWindow(Path.Combine(folder, "library")) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        window.Show(); Pump();
        var icons = (IconWorkspace)window.FindName("iconWorkspace");
        try
        {
            Capture("icons-home.png");
            ClickWindow("homeIconButton"); Pump();
            Require(icons.Visibility == Visibility.Visible && GetWindow<ScrollViewer>("workspaceHost").Visibility == Visibility.Collapsed,
                "Banner did not open the separate ICO screen");
            Require(!Get<Button>("iconSaveButton").IsEnabled, "Empty ICO can be saved");
            using var logical = Fixtures.Logical(3, 1822);
            using var input = Fixtures.Expand(logical, 4);
            string path = Path.Combine(folder, "input.png"); input.Save(path);
            var fileData = new DataObject(DataFormats.FileDrop, new[] { path });
            Drop(fileData, icons); WaitIdle();
            Require(Get<ItemsControl>("iconFrames").Items.Count == 7 && Get<Button>("iconSaveButton").IsEnabled, "Default ICO previews missing");
            Require(GetWindow<ListBox>("sourceStrip").Items.Count == 0, "ICO drop polluted the editor library");
            string output = Path.Combine(folder, "icon.ico");
            var save = icons.SaveAsync(output);
            window.Close();
            Require(window.IsVisible && icons.IsBusy, "Close bypassed ICO saving guard");
            Wait(save);
            using (var stream = File.OpenRead(output))
            {
                var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Require(decoder.Frames.Select(f => f.PixelWidth).Order().SequenceEqual(icons.GetOptions().Sizes), "Saved frames differ from chosen sizes");
            }
            byte[] originalOutput = File.ReadAllBytes(output);
            try { Wait(icons.SaveAsync(output)); throw new Exception("ICO overwritten without consent"); } catch (IOException) { }
            Require(File.ReadAllBytes(output).SequenceEqual(originalOutput), "Failed save damaged an existing file");
            var options = icons.GetOptions();
            foreach (var check in Get<WrapPanel>("iconSizes").Children.OfType<CheckBox>()) check.IsChecked = false;
            Wait(icons.RefreshFramesAsync());
            Require(!Get<Button>("iconSaveButton").IsEnabled && Get<ItemsControl>("iconFrames").Items.Count == 0, "Zero-size selection still permits export");
            Get<CheckBox>("iconCustomCheck").IsChecked = true;
            Get<IntegerInput>("iconCustomSize").Value = 51;
            Task previous = icons.RefreshFramesAsync();
            Get<IntegerInput>("iconCustomSize").Value = 37;
            Wait(icons.RefreshFramesAsync()); Wait(previous);
            Require(icons.GetOptions().Sizes.SequenceEqual(new[] { 37 }) &&
                Get<ItemsControl>("iconFrames").Items.Cast<IconWorkspace.FramePreview>().Single().Size == 37,
                "An older preview replaced the latest custom size");
            Get<CheckBox>("iconCustomCheck").IsChecked = false;
            foreach (var check in Get<WrapPanel>("iconSizes").Children.OfType<CheckBox>()) check.IsChecked = (int)check.Tag != 96;
            Get<CheckBox>("iconCustomCheck").IsChecked = true;
            Get<IntegerInput>("iconCustomSize").Value = 32;
            Require(icons.GetOptions().Sizes.Count == 7, "Duplicate custom size was not deduplicated");
            Get<CheckBox>("iconCustomCheck").IsChecked = false;
            Get<ComboBox>("iconResizeMode").SelectedIndex = 1;
            Get<ComboBox>("iconFitMode").SelectedIndex = 2;
            Wait(icons.RefreshFramesAsync());
            Require(icons.GetOptions().ResizeMode == IconResizeMode.Smooth && icons.GetOptions().FitMode == IconFitMode.Stretch,
                "ICO scale/fit controls ignored");
            Get<ComboBox>("iconResizeMode").SelectedIndex = 0;
            Get<ComboBox>("iconFitMode").SelectedIndex = 0;
            Click("iconBackButton"); Pump();
            Drop(fileData, GetWindow<Border>("iconBanner")); WaitIdle();
            Require(icons.Visibility == Visibility.Visible, "Banner drop did not enter ICO workspace");
            byte[] originalInput = File.ReadAllBytes(path);
            try { Wait(icons.SaveAsync(path, true)); throw new Exception("Source can be overwritten"); } catch (InvalidOperationException) { }
            Require(File.ReadAllBytes(path).SequenceEqual(originalInput), "ICO save changed the source");
            string bad = Path.Combine(folder, "broken.png"); File.WriteAllText(bad, "not an image");
            Wait(icons.LoadFileAsync(bad)); WaitIdle();
            Require(Get<Button>("iconSaveButton").IsEnabled && Get<TextBlock>("iconFileLabel").Text == "input.png", "Failed input destroyed the previous image");

            Click("iconBackButton"); Invoke("LoadImage", path); Pump();
            GetWindow<IntegerInput>("widthInput").Value = 8;
            GetWindow<IntegerInput>("heightInput").Value = 8;
            ClickWindow("processButton");
            var until = DateTime.UtcNow.AddSeconds(20);
            while (GetWindow<bool>("_processing") && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(!GetWindow<bool>("_processing"), "Editor processing timed out");
            var result = GetWindow<Bitmap>("_downscaledResult");
            var document = GetWindow<SourceEntry>("_activeSource");
            Invoke("ShowHome"); ClickWindow("homeIconButton"); ClickMaterial("iconEditorChoices", "materialResults"); WaitIdle();
            var picker = Get<EditorMaterialPicker>("iconEditorChoices");
            var trigger = (Button)picker.FindName("materialTrigger");
            trigger.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            Require(picker.IsOpen && picker.SourceCount == 1 && picker.ResultCount == 1,
                "ICO editor materials did not appear on hover");
            Pump();
            var popup = (System.Windows.Controls.Primitives.Popup)picker.FindName("materialPopup");
            var panel = (FrameworkElement)popup.Child;
            panel.UpdateLayout();
            var popupBitmap = new RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth), (int)Math.Ceiling(panel.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            popupBitmap.Render(panel);
            var popupEncoder = new PngBitmapEncoder(); popupEncoder.Frames.Add(BitmapFrame.Create(popupBitmap));
            using (var popupStream = File.Create(Path.Combine("artifacts", "wpf", "verification", "editor-materials.png"))) popupEncoder.Save(popupStream);
            Require(GetCursorPos(out var syntheticCursor), "Could not read cursor position");
            try
            {
                Require(SetCursorPos((int)SystemParameters.VirtualScreenLeft + 2,
                    (int)SystemParameters.VirtualScreenTop + 2), "Could not position cursor outside picker");
                Pump();
                trigger.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                panel.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
                Thread.Sleep(160); Pump();
                Require(picker.IsOpen, "Moving from trigger to popup closed the picker");
                panel.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                Thread.Sleep(160); Pump();
                Require(!picker.IsOpen, "Picker remained open after pointer left the popup");
                trigger.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
                trigger.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                Thread.Sleep(160); Pump();
                Require(!picker.IsOpen, "Picker remained open after pointer left the trigger");
            }
            finally { SetCursorPos(syntheticCursor.X, syntheticCursor.Y); Pump(); }
            Require(GetCursorPos(out var previousCursor), "Could not read cursor position");
            try
            {
                trigger.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
                var overTrigger = trigger.PointToScreen(new Point(trigger.ActualWidth / 2, trigger.ActualHeight / 2));
                Require(SetCursorPos((int)overTrigger.X, (int)overTrigger.Y), "Could not position cursor on trigger"); Pump();
                trigger.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
                typeof(EditorMaterialPicker).GetMethod("ScheduleClose", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker, null);
                Thread.Sleep(160); Pump();
                GetCursorPos(out var triggerCursor);
                Require(picker.IsOpen, $"Picker closed while real cursor remained on trigger: expected {overTrigger}, actual {triggerCursor.X},{triggerCursor.Y}, visible {trigger.IsVisible}, size {trigger.ActualWidth}x{trigger.ActualHeight}");
                var overPanel = panel.PointToScreen(new Point(12, 12));
                Require(SetCursorPos((int)overPanel.X, (int)overPanel.Y), "Could not position cursor on panel"); Pump();
                typeof(EditorMaterialPicker).GetMethod("ScheduleClose", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker, null);
                Thread.Sleep(160); Pump();
                Require(picker.IsOpen, "Picker closed while real cursor remained on panel");
                var outside = new Point(SystemParameters.VirtualScreenLeft + 2, SystemParameters.VirtualScreenTop + 2);
                Require(SetCursorPos((int)outside.X, (int)outside.Y), "Could not position cursor outside picker"); Pump();
                for (int attempt = 0; attempt < 4 && picker.IsOpen; attempt++)
                {
                    typeof(EditorMaterialPicker).GetMethod("ScheduleClose", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker, null);
                    Thread.Sleep(160); Pump();
                }
                GetCursorPos(out var outsideCursor);
                Require(!picker.IsOpen, $"Picker stayed open after cursor left: expected {outside}, actual {outsideCursor.X},{outsideCursor.Y}");
            }
            finally { SetCursorPos(previousCursor.X, previousCursor.Y); Pump(); }
            var firstFrame = Get<ItemsControl>("iconFrames").Items.Cast<IconWorkspace.FramePreview>().First();
            using (var expected = IconExporter.CreateFrame(result, firstFrame.Size, IconResizeMode.NearestNeighbor))
            {
                var pixels = new byte[firstFrame.Size * firstFrame.Size * 4];
                var wanted = new byte[pixels.Length];
                firstFrame.Image.CopyPixels(pixels, firstFrame.Size * 4, 0);
                PixelPreview.ToBitmapSource(expected).CopyPixels(wanted, firstFrame.Size * 4, 0);
                Require(pixels.SequenceEqual(wanted), "ICO preview did not use the selected editor result");
            }
            ClickMaterial("iconEditorChoices", "materialSources"); WaitIdle();
            Require(Get<TextBlock>("iconFileLabel").Text == document.Label, "Editor source selection failed");
            Click("iconBackButton");
            var generationData = (DataObject)Invoke("CreateGenerationDragData", document, document.SelectedGeneration!)!;
            Drop(generationData, GetWindow<Border>("iconBanner")); WaitIdle();
            Require(GetWindow<ListBox>("sourceStrip").Items.Count == 1 && document.Generations.Count == 1, "ICO modified editor history");

            using (var backgroundFixture = new Bitmap(32, 32))
            {
                using (var graphics = System.Drawing.Graphics.FromImage(backgroundFixture))
                {
                    graphics.Clear(System.Drawing.Color.White);
                    graphics.FillRectangle(System.Drawing.Brushes.Red, 8, 8, 16, 16);
                    graphics.FillRectangle(System.Drawing.Brushes.White, 12, 12, 8, 8);
                }
                icons.SetImage(backgroundFixture, "background.png");
                Get<CheckBox>("iconRemoveBackground").IsChecked = true;
                Wait(icons.RefreshFramesAsync());
                Require(Get<WrapPanel>("iconBackgroundSettings").Visibility == Visibility.Visible && icons.GetOptions().RemoveBackground,
                    "Background controls missing");
                var sourcePixels = new byte[32 * 32 * 4];
                ((BitmapSource)Get<System.Windows.Controls.Image>("iconSourceImage").Source).CopyPixels(sourcePixels, 32 * 4, 0);
                Require(sourcePixels[3] == 0 && sourcePixels[(16 * 32 + 16) * 4 + 3] == 0 &&
                    icons.GetOptions().BackgroundRemovalMode == BackgroundRemovalMode.GlobalColor, "Original removal mode changed");
                Get<ComboBox>("iconBackgroundMode").SelectedIndex = 1;
                WaitIdle();
                ((BitmapSource)Get<System.Windows.Controls.Image>("iconSourceImage").Source).CopyPixels(sourcePixels, 32 * 4, 0);
                Require(sourcePixels[3] == 0 && sourcePixels[(16 * 32 + 16) * 4 + 3] == 255 &&
                    icons.GetOptions().BackgroundRemovalMode == BackgroundRemovalMode.EdgeConnected, "Edge mode did not preserve enclosed color in preview");
                var backgroundOutput = Path.Combine(folder, "transparent.ico");
                Wait(icons.SaveAsync(backgroundOutput));
                using (var stream = File.OpenRead(backgroundOutput))
                {
                    var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    foreach (var frame in decoder.Frames)
                    {
                        var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
                        frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
                        Require(pixels[3] == 0, "One of the exported sizes retained the background");
                        int center = (frame.PixelHeight / 2 * frame.PixelWidth + frame.PixelWidth / 2) * 4 + 3;
                        Require(pixels[center] == 255, "Export erased enclosed color in edge mode");
                    }
                }
                Get<ComboBox>("iconBackgroundColor").SelectedIndex = 2;
                Get<ComboBox>("iconBackgroundMode").SelectedIndex = 0;
                Get<ComboBox>("iconBackgroundColor").SelectedIndex = 0;
                WaitIdle();
                ((BitmapSource)Get<System.Windows.Controls.Image>("iconSourceImage").Source).CopyPixels(sourcePixels, 32 * 4, 0);
                Require(sourcePixels[(16 * 32 + 16) * 4 + 3] == 0, "Switching back to global removal did not refresh preview");
                Get<ComboBox>("iconBackgroundColor").SelectedIndex = 2;
                Get<Slider>("iconBackgroundTolerance").Value = 12;
                Wait(icons.RefreshFramesAsync());
                Require(icons.GetOptions().BackgroundColor == System.Drawing.Color.Black && icons.GetOptions().BackgroundTolerance == 12,
                    "Background color/tolerance controls ignored");
                Get<CheckBox>("iconRemoveBackground").IsChecked = false;
                Wait(icons.RefreshFramesAsync());
                ((BitmapSource)Get<System.Windows.Controls.Image>("iconSourceImage").Source).CopyPixels(sourcePixels, 32 * 4, 0);
                Require(sourcePixels[3] == 255 && backgroundFixture.GetPixel(0, 0).A == 255, "Disabling removal did not restore original");
                Get<ComboBox>("iconBackgroundColor").SelectedIndex = 0;
                Get<Slider>("iconBackgroundTolerance").Value = 8;
            }
            Get<CheckBox>("iconRemoveBackground").IsChecked = true;

            Wait(icons.LoadFileAsync(Path.GetFullPath(Path.Combine("DomainColorTest", "Assets", "app-icon.png")))); WaitIdle();
            Capture("icons-workspace.png");
            window.Width = 560; window.Height = 700; Pump();
            Require(Get<Border>("iconSettingsPanel").ActualHeight >= 100,
                "Narrow layout hides icon settings");
            var settingsScroll = (ScrollViewer)Get<Border>("iconSettingsPanel").Child;
            Require(settingsScroll.ViewportHeight <= 220 && settingsScroll.ScrollableHeight > 0,
                "Narrow icon settings are clipped instead of scrollable");
            Require(Get<ScrollViewer>("iconScroll").ScrollableWidth == 0 && Get<StackPanel>("iconControls").ActualWidth > 400,
                "ICO screen overflows the minimum width");
            Capture("icons-narrow.png");
            Get<ItemsControl>("iconFrames").BringIntoView(); Pump(); Capture("icons-narrow-previews.png");
            Console.WriteLine("  ICO UI: banner/navigation, file and result drops, editor source/result, preview cancellation, sizes/modes, multi-frame export, file protection and narrow layout passed.");

        }
        finally { window.Close(); }

        T Get<T>(string name) where T : class => (T)icons.FindName(name);
        T GetWindow<T>(string name) => (T)(window.FindName(name) ?? typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))!;
        object? Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        void ClickWindow(string name) => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Click(string name) => Get<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void ClickMaterial(string pickerName, string panelName)
            => ((Button)((WrapPanel)Get<EditorMaterialPicker>(pickerName).FindName(panelName)).Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Wait(Task task)
        {
            var until = DateTime.UtcNow.AddSeconds(15);
            while (!task.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(task.IsCompleted, "ICO task timed out"); task.GetAwaiter().GetResult(); Pump();
        }
        void WaitIdle()
        {
            var until = DateTime.UtcNow.AddSeconds(15);
            while ((icons.IsBusy || icons.IsRendering) && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(!icons.IsBusy && !icons.IsRendering, "ICO preview timed out"); Pump();
        }
        void Drop(DataObject data, UIElement target)
        {
            var e = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [data, DragDropKeyStates.None, DragDropEffects.Copy, target, new Point(0, 0)], null)!;
            e.RoutedEvent = DragDrop.PreviewDropEvent;
            target.RaiseEvent(e);
            Require(e.Effects == DragDropEffects.Copy, "ICO drop rejected a supported image");
        }
        void Capture(string name)
        {
            GetWindow<StackPanel>("homeContent").BeginAnimation(UIElement.OpacityProperty, null);
            GetWindow<StackPanel>("homeContent").Opacity = 1;
            Pump(); window.UpdateLayout();
            var root = GetWindow<Grid>("rootLayout"); root.Background = window.Background;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine("artifacts", "wpf", "verification", name)); encoder.Save(stream);
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
