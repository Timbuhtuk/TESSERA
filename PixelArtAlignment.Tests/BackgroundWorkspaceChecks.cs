using System.Drawing.Imaging;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtDownscale;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;
using Point = System.Windows.Point;

namespace PixelArtAlignment.Tests;

internal static class BackgroundWorkspaceChecks
{
    public static void Run()
    {
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "background-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        var window = new MainWindow(Path.Combine(folder, "library")) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        window.Show(); Pump();
        var workspace = (BackgroundWorkspace)window.FindName("backgroundWorkspace");
        try
        {
            Require(!Get<Button>("backgroundSaveButton").IsEnabled, "Empty background result can be saved");
            ClickWindow("homeBackgroundButton"); Pump();
            Require(workspace.Visibility == Visibility.Visible && GetWindow<ScrollViewer>("homeScroll").Visibility == Visibility.Collapsed,
                "Background banner did not open its own screen");
            using var image = new Bitmap(9, 9);
            for (int y = 0; y < 9; y++) for (int x = 0; x < 9; x++)
                image.SetPixel(x, y, x is >= 2 and <= 6 && y is >= 2 and <= 6 && (x is 2 or 6 || y is 2 or 6)
                    ? Color.Black : Color.White);
            string input = Path.Combine(folder, "shape.png"); image.Save(input, ImageFormat.Png);
            var data = new DataObject(DataFormats.FileDrop, new[] { input });
            Drop(data, workspace); WaitIdle();
            Require(workspace.HasResult && Get<Button>("backgroundSaveButton").IsEnabled, "Dropped image did not produce a preview: " + GetWindow<TextBlock>("statusLabel").Text);
            Capture("background-workspace.png");
            Require(GetWindow<ListBox>("sourceStrip").Items.Count == 0, "Background drop polluted the editor library");
            var result = Get<PixelPreview>("backgroundResultPreview").Image!;
            Require(result.GetPixel(0, 0).A == 0 && result.GetPixel(4, 4).A == 0 && result.GetPixel(2, 2).A == 255,
                "Global removal did not remove enclosed matching color");
            Get<ComboBox>("backgroundModeInput").SelectedIndex = 1; WaitIdle();
            result = Get<PixelPreview>("backgroundResultPreview").Image!;
            Require(result.GetPixel(0, 0).A == 0 && result.GetPixel(4, 4).A == 255,
                "Edge-connected removal lost the enclosed foreground");
            Get<ComboBox>("backgroundModeInput").SelectedIndex = 0;
            Get<ComboBox>("backgroundColorInput").SelectedIndex = 2; WaitIdle();
            result = Get<PixelPreview>("backgroundResultPreview").Image!;
            Require(result.GetPixel(0, 0).A == 255 && result.GetPixel(2, 2).A == 0,
                "Manual background color was ignored");
            Get<ComboBox>("backgroundColorInput").SelectedIndex = 0;
            Get<ComboBox>("backgroundModeInput").SelectedIndex = 1; WaitIdle();
            string output = Path.Combine(folder, "transparent.png");
            Wait(workspace.SaveAsync(output));
            using (var saved = new Bitmap(output))
                Require(saved.GetPixel(0, 0).A == 0 && saved.GetPixel(4, 4).A == 255, "Saved PNG differs from preview");
            byte[] original = File.ReadAllBytes(input);
            try { Wait(workspace.SaveAsync(input, true)); throw new Exception("Source overwrite allowed"); }
            catch (InvalidOperationException) { }
            Require(File.ReadAllBytes(input).SequenceEqual(original), "Original image changed");
            try { Wait(workspace.SaveAsync(output)); throw new Exception("Existing PNG overwritten without consent"); }
            catch (IOException) { }
            string invalid = Path.Combine(folder, "broken.png"); File.WriteAllText(invalid, "not an image");
            Wait(workspace.LoadFileAsync(invalid)); WaitIdle();
            Require(workspace.HasResult && Get<TextBlock>("backgroundFileLabel").Text == "shape.png", "Failed load discarded previous image");
            window.Width = 560; Pump();
            Require(Get<Border>("backgroundSettingsPanel").ActualHeight >= 100,
                "Narrow layout hides background settings");
            Require(Grid.GetRow(Get<StackPanel>("backgroundResultPanel")) == 1, "Narrow layout did not stack previews");
            Click("backgroundBackButton"); Pump();
            Drop(data, GetWindow<Border>("backgroundBanner")); WaitIdle();
            Require(workspace.Visibility == Visibility.Visible && workspace.HasResult, "Banner drop did not open background screen");
            Click("backgroundBackButton");
            typeof(MainWindow).GetMethod("LoadImage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [input]); Pump();
            GetWindow<IntegerInput>("widthInput").Value = 8;
            GetWindow<IntegerInput>("heightInput").Value = 8;
            ClickWindow("processButton");
            var until = DateTime.UtcNow.AddSeconds(20);
            while ((bool)typeof(MainWindow).GetField("_processing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)! && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(DateTime.UtcNow < until, "Editor processing timed out");
            typeof(MainWindow).GetMethod("ShowHome", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            ClickWindow("homeBackgroundButton"); ClickMaterial("backgroundEditorChoices", "materialResults"); WaitIdle();
            Require(workspace.HasResult && Get<TextBlock>("backgroundFileLabel").Text.StartsWith("shape_result_", StringComparison.Ordinal), "Editor result was not offered to background mode");
            var picker = Get<EditorMaterialPicker>("backgroundEditorChoices");
            ((Button)picker.FindName("materialTrigger")).RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            Require(picker.IsOpen && picker.SourceCount == 1 && picker.ResultCount == 1,
                "Background editor materials did not appear on hover");

            ClickMaterial("backgroundEditorChoices", "materialSources"); WaitIdle();
            Require(Get<TextBlock>("backgroundFileLabel").Text == "shape.png", "Editor source was not offered to background mode");
            Click("backgroundBackButton");
            using (var second = new Bitmap(10, 10))
            {
                using var graphics = System.Drawing.Graphics.FromImage(second);
                graphics.Clear(Color.White);
                graphics.FillRectangle(System.Drawing.Brushes.Black, 1, 1, 6, 6);
                second.Save(Path.Combine(folder, "second.png"), ImageFormat.Png);
            }
            typeof(MainWindow).GetMethod("LoadImage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [Path.Combine(folder, "second.png")]); Pump();
            GetWindow<IntegerInput>("widthInput").Value = 8;
            GetWindow<IntegerInput>("heightInput").Value = 8;
            ClickWindow("processButton"); WaitEditor();
            GetWindow<IntegerInput>("widthInput").Value = 6;
            GetWindow<IntegerInput>("heightInput").Value = 6;
            ClickWindow("processButton"); WaitEditor();
            typeof(MainWindow).GetMethod("ShowHome", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            ClickWindow("homeBackgroundButton"); Pump();
            picker = Get<EditorMaterialPicker>("backgroundEditorChoices");
            Require(picker.SourceCount == 2 && picker.ResultCount == 3, "Picker omitted library sources or result versions");
            ((Button)picker.FindName("materialTrigger")).RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            Pump();
            var materialsPopup = (System.Windows.Controls.Primitives.Popup)picker.FindName("materialPopup");
            var materialsPanel = (FrameworkElement)materialsPopup.Child;
            materialsPanel.UpdateLayout();
            var materialsBitmap = new RenderTargetBitmap((int)Math.Ceiling(materialsPanel.ActualWidth), (int)Math.Ceiling(materialsPanel.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            materialsBitmap.Render(materialsPanel);
            var materialsEncoder = new PngBitmapEncoder(); materialsEncoder.Frames.Add(BitmapFrame.Create(materialsBitmap));
            using (var materialsStream = File.Create(Path.Combine("artifacts", "wpf", "verification", "editor-materials-all.png"))) materialsEncoder.Save(materialsStream);
            var sourceCards = ((WrapPanel)picker.FindName("materialSources")).Children.OfType<Button>().ToArray();
            var resultCards = ((WrapPanel)picker.FindName("materialResults")).Children.OfType<Button>().ToArray();
            Require(((EditorMaterial)sourceCards[0].Tag).FileLabel == "second.png" &&
                ((EditorMaterial)sourceCards[1].Tag).FileLabel == "shape.png", "Source cards are not distinct");
            sourceCards[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); WaitIdle();
            Require(Get<TextBlock>("backgroundFileLabel").Text == "shape.png" &&
                Get<PixelPreview>("backgroundSourcePreview").Image!.Width == 9, "Older source card opened the wrong material");
            sourceCards[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); WaitIdle();
            Require(Get<TextBlock>("backgroundFileLabel").Text == "second.png" &&
                Get<PixelPreview>("backgroundSourcePreview").Image!.Width == 10, "Newest source card opened the wrong material");
            Require(resultCards.Select(c => ((EditorMaterial)c.Tag).SnapshotPath).Distinct().Count() == 3,
                "Result cards point to the same version");
            var olderResult = (EditorMaterial)resultCards[^1].Tag;
            resultCards[^1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); WaitIdle();
            using (var expected = ImageLibrary.ReadBitmap(olderResult.SnapshotPath))
                Require(Get<PixelPreview>("backgroundSourcePreview").Image!.Width == expected.Width &&
                    Get<PixelPreview>("backgroundSourcePreview").Image!.Height == expected.Height,
                    "Older result card opened the wrong version");
            Click("backgroundBackButton"); ClickWindow("homeIconButton"); Pump();
            var iconPicker = (EditorMaterialPicker)window.FindName("iconWorkspace")!.GetType().GetField("iconEditorChoices", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window.FindName("iconWorkspace"))!;
            Require(iconPicker.SourceCount == 2 && iconPicker.ResultCount == 3, "ICO picker omitted library materials");            Console.WriteLine("  Background UI: standalone banner, drop, live global/edge preview, manual color, transparent PNG, source protection and narrow layout passed.");
        }
        finally { window.Close(); }

        T Get<T>(string name) where T : class => (T)workspace.FindName(name);
        T GetWindow<T>(string name) where T : class => (T)window.FindName(name);
        void ClickWindow(string name) => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Click(string name) => Get<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void ClickMaterial(string pickerName, string panelName)
            => ((Button)((WrapPanel)Get<EditorMaterialPicker>(pickerName).FindName(panelName)).Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Capture(string name)
        {
            Pump(); window.UpdateLayout();
            var root = GetWindow<Grid>("rootLayout"); root.Background = window.Background;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine("artifacts", "wpf", "verification", name)); encoder.Save(stream);
        }
        void WaitEditor()
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while ((bool)typeof(MainWindow).GetField("_processing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)! && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(DateTime.UtcNow < until, "Editor processing timed out");
        }
        void Wait(Task task)
        {
            var until = DateTime.UtcNow.AddSeconds(15);
            while (!task.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(task.IsCompleted, "Background task timed out"); task.GetAwaiter().GetResult(); Pump();
        }
        void WaitIdle()
        {
            var until = DateTime.UtcNow.AddSeconds(15);
            while ((workspace.IsBusy || workspace.IsRendering) && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(!workspace.IsBusy && !workspace.IsRendering, "Background preview timed out"); Pump();
        }
        void Drop(DataObject data, UIElement target)
        {
            var e = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [data, DragDropKeyStates.None, DragDropEffects.Copy, target, new Point(0, 0)], null)!;
            e.RoutedEvent = DragDrop.PreviewDropEvent;
            target.RaiseEvent(e);
            Require(e.Effects == DragDropEffects.Copy, "Background drop rejected supported image");
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
