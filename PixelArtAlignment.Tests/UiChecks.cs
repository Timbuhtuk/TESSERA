using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtDownscale;
using Color = System.Drawing.Color;

namespace PixelArtAlignment.Tests;

internal static class UiChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { LibraryLocationChecks.Run(); UpscaleUiChecks.Run(); WindowChromeChecks.Run(); Verify(); LibraryChecks.Run(); PixelWorkflowChecks.Run(); LibraryRemovalChecks.Run(); IconWorkspaceChecks.Run(); AsepriteUiChecks.Run(); BackgroundWorkspaceChecks.Run(); } catch (Exception e) { failure = e; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(60))) throw new Exception("WPF verification timed out");
        if (failure is not null) { Console.Error.WriteLine(failure); throw failure; }
    }

    private static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var window = new MainWindow(Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "ui-library-" + Guid.NewGuid().ToString("N")))) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        // Exercise wide layouts even when the CI desktop is narrower than the test window.
        // Native monitor bounds and normal resizing are covered by WindowChromeChecks.
        window.SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!.AddHook(AllowWideLayout);
        string directory = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification"));
        Directory.CreateDirectory(directory);
        try
        {
            window.Show();
            Pump();
            Require(!Get<Button>("processButton").IsEnabled && !Get<Button>("saveButton").IsEnabled &&
                !Get<Button>("saveAllButton").IsEnabled, "Empty state permits processing/saving");
            Require(Get<ScrollViewer>("homeScroll").Visibility == Visibility.Visible && Get<Border>("emptyLibrary").Visibility == Visibility.Visible, "Startup home or empty library missing");
            Capture("empty.png");
            window.Width = 560; Capture("home-narrow.png");
            Require(Get<ScrollViewer>("homeScroll").ScrollableWidth == 0, "Home overflows at narrow width");
            window.Width = 1220; Pump();
            using var logical = Fixtures.Logical(3, 882);
            using var source = Fixtures.Expand(logical, 4);
            string inputPath = Path.Combine(directory, "fixture.png");
            source.Save(inputPath);
            Invoke("LoadImage", inputPath);
            Require(Get<Button>("alignButton").IsEnabled, "Loading did not enable alignment");
            Require(Get<ScrollViewer>("homeScroll").Visibility == Visibility.Collapsed, "Import did not open editor");
            Click("backToLibraryButton"); Pump();
            Require(Get<Border>("emptyLibrary").Visibility == Visibility.Collapsed && Get<ItemsControl>("libraryCards").Items.Count == 1, "Home did not reflect imported source");
            var cardSource = (SourceEntry)Get<ItemsControl>("libraryCards").Items[0];
            var item = (ContentPresenter)Get<ItemsControl>("libraryCards").ItemContainerGenerator.ContainerFromIndex(0);
            item.ApplyTemplate();
            var card = (Button)VisualTreeHelper.GetChild(item, 0);
            card.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(Get<ScrollViewer>("homeScroll").Visibility == Visibility.Collapsed && ReferenceEquals(Get<SourceEntry>("_activeSource"), cardSource), "Library card did not open its source");
            Require(Get<StackPanel>("settingsPanel").Visibility == Visibility.Collapsed &&
                Get<Grid>("workspaceGrid").ColumnDefinitions.Count <= 1, "The old settings sidebar is still visible");
            window.Width = 1600; Pump();
            Require(Math.Abs(window.ActualWidth - 1600) < 1,
                $"Wide layout fixture was constrained to {window.ActualWidth} instead of 1600");
            double toolbarY = Get<Button>("backToLibraryButton").TranslatePoint(new System.Windows.Point(), window).Y;
            Require(Math.Abs(Get<Button>("gridToolButton").TranslatePoint(new System.Windows.Point(), window).Y - toolbarY) < 5 &&
                Math.Abs(Get<ComboBox>("zoomInput").TranslatePoint(new System.Windows.Point(), window).Y - toolbarY) < 5,
                "Wide editor toolbar did not fit onto one row");
            Require(Get<PixelPreview>("sourcePreview").TranslatePoint(new System.Windows.Point(), window).X >
                Get<PixelPreview>("resultPreview").TranslatePoint(new System.Windows.Point(), window).X + 200,
                "Wide editor previews were not placed side by side");
            Capture("editor-wide.png");
            window.Width = 1220; Pump();
            var action = Get<Button>("processButton");
            Require(action.Visibility == Visibility.Visible && action.Content?.ToString() == "Обработать" &&
                !Get<WrapPanel>("editorToolbar").Children.Contains(action), "Processing action remained in the main toolbar");
            Click("fileMenuButton");
            Require(Get<System.Windows.Controls.Primitives.Popup>("fileMenuPopup").IsOpen &&
                Get<Button>("openButton").IsEnabled && !Get<Button>("saveButton").IsEnabled &&
                !Get<Button>("saveAllButton").IsEnabled, "File menu did not expose Open and Save");
            Get<System.Windows.Controls.Primitives.Popup>("fileMenuPopup").IsOpen = false;
            Click("gridToolButton"); Pump();
            var gridTool = Window.GetWindow(Get<IntegerInput>("gridCellInput"));
            Require(gridTool is not null && gridTool != window && gridTool.IsVisible, "Grid tool did not open a separate window");
            Click("sizeToolButton"); Pump();
            var sizeTool = Window.GetWindow(Get<IntegerInput>("widthInput"));
            Require(sizeTool is not null && sizeTool != window && sizeTool != gridTool &&
                ReferenceEquals(Window.GetWindow(Get<Slider>("brightnessInput")), sizeTool) &&
                ReferenceEquals(Window.GetWindow(Get<CheckBox>("spriteInput")), sizeTool), "Size, frame and pixel tools were not grouped");
            Click("colorToolButton"); Pump();
            var colorTool = Window.GetWindow(Get<ComboBox>("paletteInput"));
            Require(colorTool is not null && colorTool != sizeTool, "Color tool did not open a separate window");
            Click("profileToolButton"); Pump();
            var profileTool = Window.GetWindow(Get<TextBox>("processingLog"));
            Require(profileTool is not null && profileTool != colorTool &&
                ReferenceEquals(Window.GetWindow(Get<RadioButton>("sceneMode")), profileTool) &&
                ReferenceEquals(Window.GetWindow(action), profileTool), "Profile and processing tools were not grouped");
            CaptureTool(sizeTool!, "size-tool.png");
            CaptureTool(colorTool!, "color-tool.png");
            CaptureTool(profileTool!, "mode-tool.png");
            foreach (var tool in new[] { gridTool, sizeTool, colorTool, profileTool }) tool!.Close();
            Require(ReferenceEquals(Window.GetWindow(Get<IntegerInput>("widthInput")), window), "Tool settings were lost after closing their window");
            Get<IntegerInput>("widthInput").Value = 13;
            Get<IntegerInput>("heightInput").Value = 7;
            Get<ComboBox>("paletteInput").SelectedIndex = 4;
            Get<IntegerInput>("quantizationColorsInput").Value = 7;
            Get<CheckBox>("colorWeightsInput").IsChecked = true;
            Get<IntegerInput>("gridCellInput").Value = 4;
            Click("alignButton");
            Require(Get<bool>("_processing") && !Get<Button>("openButton").IsEnabled && !Get<StackPanel>("settingsPanel").IsEnabled, "Busy state did not lock mutable inputs");
            Wait();
            Require(Get<bool>("_resultIsAlignment"), "Alignment was not run");
            var output = Get<Bitmap>("_downscaledResult");
            Require(AlignmentQuality.Measure(output, source, 4).Score == 100, "WPF mixed alignment with downscale/color settings");
            Get<IntegerInput>("widthInput").Value = 15;
            Require(!Get<bool>("_resultStale"), "Downscale size invalidated alignment");
            Get<IntegerInput>("gridCellInput").Value = 5;
            Require(Get<bool>("_resultStale"), "Grid change did not invalidate alignment");
            Get<IntegerInput>("gridCellInput").Value = 4;
            Click("alignButton");
            Wait();
            window.Width = 1600; Pump();
            Require(Get<Button>("saveAllButton").IsEnabled, "Batch save was not enabled after processing");
            Require(ReferenceEquals(Get<Border>("sourceTray").Parent, Get<Grid>("historyRow")) &&
                ReferenceEquals(Get<TextBlock>("sourceCaption").Parent, Get<ListBox>("sourceStrip").Parent) &&
                Get<TextBlock>("sourceCaption").Text.StartsWith("ИСХОДНИК ·") &&
                Get<ListBox>("sourceStrip").TranslatePoint(new System.Windows.Point(), window).X >
                Get<ListBox>("generationStrip").TranslatePoint(new System.Windows.Point(), window).X + 200 &&
                Math.Abs(Get<ListBox>("generationStrip").ActualWidth - Get<ListBox>("sourceStrip").ActualWidth) < 20,
                "Wide editor did not place source history beside result history");
            Capture("history-wide.png");
            string batchDirectory = Path.Combine(directory, "all-results-" + Guid.NewGuid().ToString("N"));
            Invoke("SaveAllResults", batchDirectory);
            int batchCount = Get<SourceEntry>("_activeSource").Generations.Count;
            var exported = Directory.GetFiles(batchDirectory, "*.png");
            Require(exported.Length == batchCount, "Batch save missed results");
            foreach (string path in exported)
            {
                using var image = new Bitmap(path);
                EqualPixels(image, Get<Bitmap>("_downscaledResult"), "Batch export changed a saved result");
            }
            Invoke("SaveAllResults", batchDirectory);
            Require(Directory.GetFiles(batchDirectory, "*.png").Length == batchCount * 2, "Batch save replaced earlier exports");
            window.Width = 1220; Pump();
            Capture("alignment.png");
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            Capture("minimum.png");
            var sourceSelector = Get<ComboBox>("operationSourceInput");
            Require(sourceSelector.TranslatePoint(new System.Windows.Point(sourceSelector.ActualWidth, 0), window).X <
                window.ActualWidth - 8, "Narrow toolbar clips the input selector");
            Require(ReferenceEquals(Get<Border>("sourceTray").Parent, Get<Grid>("previewArea")) &&
                Grid.GetRow(Get<Border>("sourceTray")) == 2, "Narrow editor did not return source history below previews");
            Require(Get<PixelPreview>("sourcePreview").TranslatePoint(new System.Windows.Point(), window).Y >
                Get<PixelPreview>("resultPreview").TranslatePoint(new System.Windows.Point(), window).Y + 100,
                "Narrow editor previews were not stacked");
            Require(Get<PixelPreview>("resultPreview").ActualWidth > 350 && Get<PixelPreview>("sourcePreview").ActualHeight > 120, "Previews collapsed at minimum window size");
            string alignedPath = Path.Combine(directory, "aligned.png");
            Invoke("SaveResult", alignedPath);
            using (var saved = new Bitmap(alignedPath)) EqualPixels(saved, Get<Bitmap>("_downscaledResult"), "Alignment PNG roundtrip");
            Reject("SaveResult", inputPath);
            Reject("SaveResult", Path.Combine(directory, "alpha.jpg"));
            using (var original = new Bitmap(inputPath)) EqualPixels(original, source, "Original was overwritten");
            Click("profileToolButton"); Pump();
            Click("processButton");
            Wait();
            Window.GetWindow(action)!.Close();
            Require(!Get<bool>("_resultIsAlignment") && Get<Bitmap>("_downscaledResult").Width == 15, "Downscale no longer works independently");
            var options = (DownscaleOptions)Invoke("BuildOptionsFromUi")!;
            Require(options.QuantizationColors == 64 && !options.UseColorWeights &&
                options.Quantization == QuantizationMethod.KMeansLab, "Process did not apply the selected scene profile");
            var expected = new PixelArtDownscaler().Process(source, options);
            using (expected.Downscaled)
            using (expected.CroppedSource) EqualPixels(expected.Downscaled, Get<Bitmap>("_downscaledResult"), "WPF result differs from shared algorithm");
            Get<IntegerInput>("gridCellInput").Value = 5;
            Require(!Get<bool>("_resultStale"), "Grid size invalidated downscale");
            Get<CheckBox>("colorWeightsInput").IsChecked = true;
            Require(Get<bool>("_resultStale"), "Color options did not invalidate downscale");
            Get<ComboBox>("paletteInput").SelectedIndex = 4;
            Get<ComboBox>("operationSourceInput").SelectedIndex = 0;
            Click("scaleOnlyButton"); Wait();
            var sizeOnly = Get<Bitmap>("_downscaledResult");
            Require(sizeOnly.Width == 15 && sizeOnly.Height == 7, "Independent scaling used the wrong size");
            var sourceColors = new HashSet<int>();
            for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++) sourceColors.Add(source.GetPixel(x, y).ToArgb());
            for (int y = 0; y < sizeOnly.Height; y++) for (int x = 0; x < sizeOnly.Width; x++)
                Require(sourceColors.Contains(sizeOnly.GetPixel(x, y).ToArgb()), "Independent scaling changed a source color");
            var sizeGeneration = Get<SourceEntry>("_activeSource").SelectedGeneration!;
            Get<ComboBox>("operationSourceInput").SelectedIndex = 1;
            Click("colorOnlyButton"); Wait();
            var colorGeneration = Get<SourceEntry>("_activeSource").SelectedGeneration!;
            var colorOnly = Get<Bitmap>("_downscaledResult");
            Require(colorGeneration.ParentGenerationId == sizeGeneration.Id && colorOnly.Width == 15 && colorOnly.Height == 7,
                "Color operation did not use the selected result without resizing it");
            var paletteColors = Palettes.GetPalette(PaletteKind.GameBoy).ToHashSet();
            for (int y = 0; y < colorOnly.Height; y++) for (int x = 0; x < colorOnly.Width; x++)
                Require(paletteColors.Contains(colorOnly.GetPixel(x, y).ToArgb() & 0xFFFFFF), "Color operation ignored the palette");
            Get<ComboBox>("operationSourceInput").SelectedIndex = 0;
            Click("colorOnlyButton"); Wait();
            Require(Get<Bitmap>("_downscaledResult").Size == source.Size && Get<SourceEntry>("_activeSource").SelectedGeneration!.ParentGenerationId is null,
                "Color operation on the source unexpectedly scaled or chained a result");
            Click("sizeToolButton"); Pump();
            Get<RadioButton>("manualInput").IsChecked = true;
            Get<Slider>("brightnessInput").Value = 73;
            Require(((DownscaleOptions)Invoke("BuildOptionsFromUi")!).ManualCriteria?.TargetBrightness == .73, "Manual criteria mapping failed");
            Capture("editor-tools.png");
            Window.GetWindow(Get<IntegerInput>("widthInput"))!.Close();
            var selectedParent = Get<SourceEntry>("_activeSource").SelectedGeneration!;
            var beforeMode = (DownscaleOptions)Invoke("BuildOptionsFromUi")!;
            Get<RadioButton>("detailsMode").IsChecked = true;
            options = (DownscaleOptions)Invoke("BuildOptionsFromUi")!;
            Require(options.SpriteMode == beforeMode.SpriteMode &&
                options.Quantization == beforeMode.Quantization &&
                options.Palette == beforeMode.Palette &&
                options.ManualCriteria?.TargetBrightness == .73,
                "Choosing a mode changed settings before processing");
            Get<ComboBox>("operationSourceInput").SelectedIndex = 1;
            Click("profileToolButton"); Pump();
            Click("processButton"); Wait();
            Window.GetWindow(action)!.Close();
            options = (DownscaleOptions)Invoke("BuildOptionsFromUi")!;
            Require(options.SpriteMode && options.Quantization == QuantizationMethod.MedianCut &&
                options.Palette == PaletteKind.None && options.AlphaThreshold == 75 &&
                options.QuantizationColors == 64 && !options.UseColorWeights &&
                options.TargetWidth == 15 && options.TargetHeight == 7,
                "Processing did not apply the details profile");
            Require(Get<SourceEntry>("_activeSource").SelectedGeneration?.ParentGenerationId == selectedParent.Id,
                "Mode processing ignored the selected result");
            Require(!Get<CheckBox>("ditheringInput").IsEnabled, "Sprite mode permits dithering");
            Get<ComboBox>("operationSourceInput").SelectedIndex = 0;
            Get<CheckBox>("aspectLock").IsChecked = true;
            Get<IntegerInput>("widthInput").Value = 24;
            Require(Get<IntegerInput>("heightInput").Value == (int)Math.Round(24.0 * source.Height / source.Width), "Aspect ratio failed");
            Get<CheckBox>("detectGridInput").IsChecked = true;
            Require(!Get<IntegerInput>("gridCellInput").IsEnabled, "Autodetection did not disable manual pitch");
            Get<CheckBox>("detectGridInput").IsChecked = false;
            VerifyPreview();
            // A new source starts without results; importing an existing source now restores its history.
            string newInputPath = Path.Combine(directory, "second-fixture.png");
            source.Save(newInputPath);
            Invoke("LoadImage", newInputPath);
            Require(!Get<Button>("saveButton").IsEnabled && Get<PixelPreview>("resultPreview").Image is null, "Loading retained the former output");
            Reject("LoadImage", Path.Combine(directory, "missing.png"));
            Require(Get<Bitmap>("_sourceImage").Width == source.Width, "Failed load damaged source");
            // These checks use the generated fixtures and require no external image collection.
            window.Width = 1220; window.Height = 880;
            Click("backToLibraryButton"); Pump();
            Capture("home-library.png");
            Get<ScrollViewer>("homeScroll").ScrollToBottom(); Capture("home-library-bottom.png");
            window.Width = 560; Get<ScrollViewer>("homeScroll").ScrollToTop(); Capture("home-library-narrow.png");
            Require(window.CardWidth > 450 && window.CardWidth < 530, "Narrow library cards do not occupy one column");
            Console.WriteLine("  WPF: alignment, downscale parity, profiles, color options, alpha, saving, input bounds, preview zoom and layouts passed.");
        }
        finally { window.Close(); }

        T Get<T>(string name) => (T)(window.FindName(name) ?? typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))!;
        object? Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        void Click(string name) => Get<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Reject(string method, string path)
        {
            try { Invoke(method, path); } catch (TargetInvocationException) { return; }
            throw new Exception($"{method} unexpectedly accepted {path}");
        }
        void Wait()
        {
            var until = DateTime.UtcNow.AddSeconds(25);
            while (Get<bool>("_processing") && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(!Get<bool>("_processing"), "Processing timeout");
        }
        void CaptureTool(Window tool, string name)
        {
            tool.UpdateLayout(); Pump();
            var screenshot = new RenderTargetBitmap((int)Math.Ceiling(tool.ActualWidth), (int)Math.Ceiling(tool.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            screenshot.Render(tool);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(screenshot));
            using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
        }
        void Capture(string name)
        {
            window.UpdateLayout(); Pump(); window.UpdateLayout();
            var content = Get<Grid>("rootLayout");
            content.Background = window.Background;
            var screenshot = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            screenshot.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(screenshot));
            using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
        }
    }

    private static void VerifyPreview()
    {
        using var alpha = new Bitmap(3, 1, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        alpha.SetPixel(0, 0, Color.FromArgb(0, 18, 39, 72));
        alpha.SetPixel(1, 0, Color.FromArgb(128, 170, 80, 35));
        alpha.SetPixel(2, 0, Color.FromArgb(255, 33, 90, 190));
        var converted = PixelPreview.ToBitmapSource(alpha);
        byte[] bytes = new byte[12]; converted.CopyPixels(bytes, 12, 0);
        for (int x = 0; x < 3; x++)
        {
            var c = alpha.GetPixel(x, 0);
            Require(bytes[x * 4] == c.B && bytes[x * 4 + 1] == c.G && bytes[x * 4 + 2] == c.R && bytes[x * 4 + 3] == c.A, "WPF conversion changed BGRA/alpha");
        }
        Require(converted.IsFrozen, "Preview retains a mutable source");
        var input = new IntegerInput { Minimum = 2, Maximum = 256, Value = 8 };
        var editor = (TextBox)((Grid)input.Content).Children[0];
        editor.Text = "999"; input.Commit(); Require(input.Value == 256 && editor.Text == "256", "Input did not clamp overflow");
        editor.Text = ""; input.Commit(); Require(editor.Text == "256", "Empty input lost last valid value");
        editor.Text = "12"; Require(input.Value == 12, "Typed value was not propagated");
        using var pixels = new Bitmap(2, 1);
        pixels.SetPixel(0, 0, Color.Red); pixels.SetPixel(1, 0, Color.Blue);
        var preview = new PixelPreview { Image = pixels, Zoom = 8, PreviewBackground = 1 };
        preview.Measure(new System.Windows.Size(80, 40)); preview.Arrange(new Rect(0, 0, 80, 40)); preview.UpdateLayout();
        var rendered = new RenderTargetBitmap(80, 40, 96, 96, PixelFormats.Pbgra32); rendered.Render(preview);
        byte[] raster = new byte[80 * 40 * 4]; rendered.CopyPixels(raster, 80 * 4, 0);
        int redCount = 0, blueCount = 0;
        for (int i = 0; i < raster.Length; i += 4)
        {
            if (raster[i] == 0 && raster[i + 1] == 0 && raster[i + 2] == 255) redCount++;
            if (raster[i] == 255 && raster[i + 1] == 0 && raster[i + 2] == 0) blueCount++;
        }
        Require(redCount == 64 && blueCount == 64, $"800% preview pixels are blurred or scaled incorrectly: {redCount}/{blueCount}");
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static nint AllowWideLayout(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0x24) return 0; // WM_GETMINMAXINFO
        var bounds = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        bounds.MaxTrackSize = new NativePoint { X = 4096, Y = 4096 };
        Marshal.StructureToPtr(bounds, lParam, false);
        handled = true;
        return 0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo
    {
        public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
    private static void EqualPixels(Bitmap actual, Bitmap expected, string context)
    {
        Require(actual.Size == expected.Size, context + ": dimensions");
        for (int y = 0; y < actual.Height; y++)
            for (int x = 0; x < actual.Width; x++)
                Require(actual.GetPixel(x, y).ToArgb() == expected.GetPixel(x, y).ToArgb(), context + $": pixel {x},{y}");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
