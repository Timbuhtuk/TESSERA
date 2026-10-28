using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using DomainColorTest;
using PixelArtDownscale;
using Tessera.WpfStyles.Themes;
using Color = System.Drawing.Color;
using Point = System.Windows.Point;
using MediaColor = System.Windows.Media.Color;

namespace PixelArtAlignment.Tests;

/// <summary>Independent feature oracles. Native input and visual review remain a separate QA gate.</summary>
internal static class ToolImprovementsChecks
{
    internal static readonly string ArtifactRoot = Path.GetFullPath(Path.Combine("artifacts", "dictated-qa", "TestArtifacts"));
    internal const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(Action<string, Action> check)
    {
        Directory.CreateDirectory(ArtifactRoot);
        check("Fixed palette dithering changes pixels, preserves alpha/source and respects mode", VerifyDithering);
        check("Every static checkbox has an explanatory tooltip", VerifyCheckboxSources);
        FilterQualityChecks.Run(check);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                check("Theme backgrounds, crop region, comparison fit and color outline", VerifyEditor);
                check("Real palette and quantizer diagonal samples match processing", PaletteComparisonChecks.Run);
                check("Exact RGB marching outline, scrolled viewport clipping and same visual fit", () => VerifyPreviewSelection(ArtifactRoot));
                check("Docked and floating info picker updates dialog, basis highlight and lifetime", InfoPickerChecks.Run);
                check("Background custom color and eraser export/undo/reprocessing", BackgroundEditingChecks.Run);
                check("Background history keeps loaded source/result parent, pixels and settings", BackgroundHistoryChecks.Run);
                check("Project Tile Inspector nested/shared scope and loaded-result provenance", ProjectTileChecks.Run);
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(3))) throw new Exception("Tool improvements WPF checks timed out");
        if (failure is not null) throw new Exception(failure.ToString(), failure);
    }

    private static void VerifyDithering()
    {
        using var source = new Bitmap(128, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                source.SetPixel(x, y, Color.FromArgb(new[] { 0, 1, 127, 255 }[y % 4],
                    x * 255 / 127, (x * 7 + y * 19) % 256, (255 - x * 2 + y * 5) % 256));
        int[] original = Pixels(source);
        foreach (PaletteKind kind in new[] { PaletteKind.DB16, PaletteKind.DB32, PaletteKind.NES, PaletteKind.GameBoy })
        {
            foreach (bool sprite in new[] { false, true })
            {
                DownscaleOptions Options(bool dither) => new()
                {
                    IndependentColorMode = IndependentColorMode.Palette, Palette = kind,
                    EnableDithering = dither, SpriteMode = sprite, ThreadCount = 1
                };
                using var plain = IndependentImageProcessor.ApplyColors(source, Options(false));
                using var dithered = IndependentImageProcessor.ApplyColors(source, Options(true));
                Require(Pixels(plain).Zip(Pixels(dithered)).Count(pair => pair.First != pair.Second) > 10,
                    $"{kind} dithering had no material effect (sprite={sprite})");
                var allowed = Palettes.GetPalette(kind).ToHashSet();
                for (int q = 0; q < original.Length; q++)
                {
                    int actual = dithered.GetPixel(q % source.Width, q / source.Width).ToArgb();
                    Require((actual & unchecked((int)0xFF000000)) == (original[q] & unchecked((int)0xFF000000)), "Dithering changed alpha");
                    Require((uint)original[q] >> 24 == 0 ? actual == original[q] : allowed.Contains(actual & 0xFFFFFF),
                        "Dithering modified hidden RGB or emitted a color outside the chosen palette");
                }
                dithered.Save(Path.Combine(ArtifactRoot, $"dither-{kind}-{sprite}.png"));
            }
        }
        foreach (PaletteKind kind in new[] { PaletteKind.None, PaletteKind.Step })
        {
            using var plain = IndependentImageProcessor.ApplyColors(source, new() { IndependentColorMode = IndependentColorMode.Palette, Palette = kind });
            using var dithered = IndependentImageProcessor.ApplyColors(source, new() { IndependentColorMode = IndependentColorMode.Palette, Palette = kind, EnableDithering = true });
            Require(Pixels(plain).SequenceEqual(Pixels(dithered)), "Dithering unexpectedly affects None/Step palette");
        }
        using var quantized = IndependentImageProcessor.ApplyColors(source, new()
        {
            IndependentColorMode = IndependentColorMode.Quantization, Palette = PaletteKind.GameBoy,
            Quantization = QuantizationMethod.MedianCut, QuantizationColors = 8, EnableDithering = true
        });
        Require(Pixels(quantized).Where(c => (uint)c >> 24 > 0).Select(c => c & 0xFFFFFF).Distinct().Count() <= 8,
            "Quantization-only mode applied palette/dither instead of honoring color count");
        Require(Pixels(source).SequenceEqual(original), "Color pipeline mutated the original bitmap");
        source.Save(Path.Combine(ArtifactRoot, "dither-original.png"));
    }

    private static void VerifyCheckboxSources()
    {
        string root = FindRepository();
        var inventory = new List<object>();
        string[] codeFiles = Directory.GetFiles(Path.Combine(root, "DomainColorTest"), "*.cs");
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "DomainColorTest"), "*.xaml", SearchOption.AllDirectories)
                     .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)))
        {
            var document = XDocument.Load(path);
            foreach (var box in document.Descendants().Where(e => e.Name.LocalName == "CheckBox"))
            {
                string? hint = box.Attribute("ToolTip")?.Value;
                bool structuredHint = box.Elements().Any(e => e.Name.LocalName == "CheckBox.ToolTip" && !string.IsNullOrWhiteSpace(e.Value));
                string name = box.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name")?.Value ?? "(unnamed)";
                string? authoredInCode = codeFiles.SelectMany(file => File.ReadLines(file).Where(line =>
                    line.Contains(name + ".ToolTip =") && !line.Contains("= null")).Select(line => Path.GetFileName(file) + ": " + line.Trim())).FirstOrDefault();
                Require(!string.IsNullOrWhiteSpace(hint) || structuredHint || authoredInCode is not null,
                    $"Checkbox {name} in {path} has neither a XAML tooltip nor a runtime assignment");
                inventory.Add(new { file = Path.GetRelativePath(root, path), name, tooltip = hint ?? authoredInCode ?? box.Value });
            }
        }
        Require(inventory.Count >= 17, "Checkbox inventory unexpectedly omitted application surfaces");
        File.WriteAllText(Path.Combine(ArtifactRoot, "checkbox-source-inventory.json"),
            System.Text.Json.JsonSerializer.Serialize(inventory, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static void VerifyEditor()
    {
        string run = Path.Combine(ArtifactRoot, "editor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        using var source = new Bitmap(23, 17, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++) source.SetPixel(x, y, Color.FromArgb(255, x * 10, y * 14, (x + y) * 6));
        string input = Path.Combine(run, "crop-source.png");
        source.Save(input);
        var window = new MainWindow(Path.Combine(run, "library")) { ShowActivated = false };
        try
        {
            Console.WriteLine("  Tool editor: showing window");
            window.Show(); Pump();
            Console.WriteLine("  Tool editor: opening Settings");
            var updateButton = Get<Button>(window, "updateButton");
            Require(!updateButton.IsVisible, "Update action is still visible outside Settings");
            Call(window, "OpenAppearance", null, new RoutedEventArgs()); Pump();
            var settings = window.OwnedWindows.OfType<AppearanceWindow>().Single();
            Require(ReferenceEquals(Get<Button>(settings, "updateButton"), updateButton) && updateButton.IsVisible,
                "Settings does not expose the actual update action");
            settings.Close(); Pump();
            Console.WriteLine("  Tool editor: reopening Settings");
            Call(window, "OpenAppearance", null, new RoutedEventArgs()); Pump();
            settings = window.OwnedWindows.OfType<AppearanceWindow>().Single();
            Require(Get<Button>(settings, "updateButton").IsVisible, "Reopening Settings lost the update action");
            settings.Close(); Pump();
            Console.WriteLine("  Tool editor: loading source");
            Call(window, "LoadImage", input); Pump();
            var sourcePreview = Get<PixelPreview>(window, "sourcePreview");
            Console.WriteLine("  Tool editor: checking theme backgrounds");
            VerifyPreviewBackgrounds(sourcePreview);
            Console.WriteLine("  Tool editor: checking crop outlines");
            NavigationTest.Click(window, "sizeMenuItem"); Pump();
            Get<CheckBox>(window, "spriteInput").IsChecked = false;
            Get<IntegerInput>(window, "widthInput").Value = 5;
            Get<IntegerInput>(window, "heightInput").Value = 4;
            foreach (int horizontal in new[] { 0, 1, 2 })
                foreach (int vertical in new[] { 0, 1, 2 })
                {
                    Get<ComboBox>(window, "cropHorizontalInput").SelectedIndex = horizontal;
                    Get<ComboBox>(window, "cropVerticalInput").SelectedIndex = vertical;
                    Call(window, "UpdateSizeHint"); Pump();
                    Rect expected = new(horizontal == 1 ? 0 : horizontal == 2 ? 3 : 1,
                        vertical == 1 ? 0 : vertical == 2 ? 1 : 0, 20, 16);
                    Require(sourcePreview.SelectedRegion == expected, "Source crop outline does not match known integer region");
                    using var actual = IndependentImageProcessor.Scale(source, new()
                    {
                        TargetWidth = 5, TargetHeight = 4, CropHorizontal = (CropHorizontalAlignment)horizontal,
                        CropVertical = (CropVerticalAlignment)vertical
                    });
                    using var cropped = source.Clone(new System.Drawing.Rectangle((int)expected.X, (int)expected.Y, 20, 16), source.PixelFormat);
                    using var oracle = IndependentImageProcessor.Scale(cropped, new() { TargetWidth = 5, TargetHeight = 4, SpriteMode = true });
                    Require(Pixels(actual).SequenceEqual(Pixels(oracle)), "Crop outline and actual Scale sample different source pixels");
                }
            NavigationTest.Click(window, "colorMenuItem"); Pump();
            Require(sourcePreview.SelectedRegion is null, "Crop outline stayed visible after switching tools");
            var liveFrame = Get<Border>(window, "livePreviewFrame");
            var liveResult = Get<PixelPreview>(window, "resultPreview");
            var previewUntil = DateTime.UtcNow.AddSeconds(10);
            while (liveFrame.Visibility != Visibility.Visible && DateTime.UtcNow < previewUntil) { Pump(); Thread.Sleep(20); }
            Require(liveFrame.Visibility == Visibility.Visible && liveResult.Image is not null,
                "Color preview did not appear in the main result pane");
            NavigationTest.Click(window, "sizeMenuItem"); Pump();
            Require(liveFrame.Visibility == Visibility.Collapsed && liveResult.Image is null,
                "Leaving the color tool kept its live result on the canvas");
            Get<CheckBox>(window, "spriteInput").IsChecked = true;
            Call(window, "UpdateSizeHint");
            Require(sourcePreview.SelectedRegion is null, "Whole-frame mode retained a crop border");
            Call(window, "ScaleOnlyImage", null, EventArgs.Empty);
            Console.WriteLine("  Tool editor: waiting for size processing");
            var processingTimeout = System.Diagnostics.Stopwatch.StartNew();
            while ((bool)Field(window, "_processing")!)
            {
                Pump(); Thread.Sleep(5);
                Require(processingTimeout.Elapsed < TimeSpan.FromSeconds(30), "Independent size operation did not finish");
            }
            window.AlternativePreview = true; Pump();
            Console.WriteLine("  Tool editor: checking comparison keys");
            Require(Get<FrameworkElement>(window, "previewPair").Visibility == Visibility.Collapsed &&
                Get<PixelPreview>(window, "comparisonPreview").Visibility == Visibility.Visible, "Alternative view did not occupy the preview");
            var comparison = Get<PixelPreview>(window, "comparisonPreview");
            Require(ReferenceEquals(comparison.Image, Field(window, "_downscaledResult")), "Alternative preview did not default to current result");
            KeyEventArgs KeyEvent(Key key, bool up = false) => new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, key) { RoutedEvent = up ? Keyboard.PreviewKeyUpEvent : Keyboard.PreviewKeyDownEvent };
            foreach (Key key in new[] { Key.LeftAlt, Key.RightAlt, Key.LeftAlt })
            {
                Call(window, "ComparisonKeyDown", window, KeyEvent(key));
                Require(ReferenceEquals(comparison.Image, Field(window, "_sourceImage")), "Bare Alt event did not show original");
                Call(window, "ComparisonKeyUp", window, KeyEvent(key, true));
                Require(ReferenceEquals(comparison.Image, Field(window, "_downscaledResult")), "Alt release event did not restore result");
            }
            Call(window, "ComparisonKeyDown", window, KeyEvent(Key.LeftAlt));
            Call(window, "ComparisonKeyDown", window, KeyEvent(Key.RightAlt));
            Call(window, "ComparisonKeyUp", window, KeyEvent(Key.LeftAlt, true));
            Require(ReferenceEquals(comparison.Image, Field(window, "_sourceImage")), "Releasing one Alt lost a still-held second Alt event");
            Call(window, "ComparisonKeyUp", window, KeyEvent(Key.RightAlt, true));
            Call(window, "ComparisonKeyDown", window, KeyEvent(Key.LeftAlt));
            Call(window, "ComparisonKeyDown", window, KeyEvent(Key.F));
            Require(ReferenceEquals(comparison.Image, Field(window, "_downscaledResult")), "Alt menu chord did not reset comparison");
            Call(window, "ComparisonKeyDown", window, KeyEvent(Key.LeftAlt));
            typeof(Window).GetMethod("OnDeactivated", Private)!.Invoke(window, new object[] { EventArgs.Empty });
            Require(ReferenceEquals(comparison.Image, Field(window, "_downscaledResult")), "Deactivation did not reset held-original state");
            window.AlternativePreview = false;

            Get<RadioButton>(window, "paletteColorModeInput").IsChecked = true;
            Get<ComboBox>(window, "paletteInput").SelectedIndex = 0;
            Call(window, "UpdateDependentControls");
            Require(Get<CheckBox>(window, "ditheringInput").IsEnabled, "Independent palette dithering still depends on whole-frame/sprite state");
            Get<CheckBox>(window, "ditheringInput").IsChecked = true;
            var savedColorSettings = (EditorSettings)Call(window, "CaptureSettings")!;
            Require(savedColorSettings.Downscale.SpriteMode && savedColorSettings.Downscale.EnableDithering &&
                savedColorSettings.Downscale.IndependentColorMode == IndependentColorMode.Palette,
                "Independent palette fixture did not capture sprite sizing plus active dithering");
            Call(window, "SetProcessingState", true);
            Call(window, "RestoreSettings", savedColorSettings);
            Call(window, "SetProcessingState", false); Pump();
            Require(Get<CheckBox>(window, "ditheringInput").IsEnabled && Get<CheckBox>(window, "ditheringInput").IsChecked == true,
                "Processing/history restore disabled independent palette dithering or discarded its saved choice");
            var boxes = Descendants(window).OfType<CheckBox>().ToArray();
            Require(boxes.Length >= 10, "Runtime checkbox inventory was incomplete");
            foreach (var box in boxes)
            {
                string? tooltip = box.ToolTip as string ?? (box.ToolTip as ToolTip)?.Content as string;
                Require(!string.IsNullOrWhiteSpace(tooltip), $"Runtime checkbox {box.Name}/{box.Content} lacks explanatory text");
            }
            Capture(window, Path.Combine(run, "editor.png"));
            window.Width = window.MinWidth; window.Height = window.MinHeight; Pump();
            Capture(window, Path.Combine(run, "editor-minimum.png"));
        }
        catch (Exception ex) { Console.WriteLine("  Tool editor failed: " + ex); throw; }
        finally { Console.WriteLine("  Tool editor: closing window"); window.Close(); Pump(); }
    }

    internal static void VerifyPreviewBackgrounds(PixelPreview preview)
    {
        var previous = ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { TesseraTheme.Dark, TesseraTheme.Light })
            {
                AppearanceResources.Apply(theme); Pump();
                MediaColor Resource(string key) => ((SolidColorBrush)(preview.TryFindResource(key) ?? AppearanceResources.Shared[key])).Color;
                int distinctModes = 0;
                for (int mode = 0; mode < 4; mode++)
                {
                    preview.PreviewBackground = mode;
                    bool light = mode is 2 or 3;
                    string firstKey = light ? theme == TesseraTheme.Light ? "Ts.Raised" : "Ts.Text"
                        : theme == TesseraTheme.Light ? "Ts.Text" : "Ts.Canvas";
                    if (mode is 1 or 2)
                        Require(preview.Background is SolidColorBrush solid && solid.Color == Resource(firstKey),
                            $"Solid preview mode {mode} does not use the current theme");
                    else
                    {
                        Require(preview.Background is DrawingBrush, $"Checkerboard mode {mode} is missing");
                        var brush = (DrawingBrush)preview.Background;
                        var colors = ((DrawingGroup)brush.Drawing).Children.OfType<GeometryDrawing>()
                            .Select(d => ((SolidColorBrush)d.Brush).Color).Distinct().ToArray();
                        string secondKey = light ? theme == TesseraTheme.Light ? "Ts.Panel" : "Ts.TextSecondary"
                            : theme == TesseraTheme.Light ? "Ts.TextSecondary" : "Ts.Panel";
                        Require(colors.Length == 2 && colors.Contains(Resource(firstKey)) && colors.Contains(Resource(secondKey)),
                            $"Checkerboard mode {mode} does not use two current-theme colors");
                    }
                    distinctModes++;
                }
                Require(distinctModes == 4, "Preview did not support all four background modes");
            }
        }
        finally { AppearanceResources.Apply(previous); Pump(); }
    }

    private static void VerifyPreviewSelection(string directory)
    {
        using var source = new Bitmap(4, 2, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var selected = Color.FromArgb(30, 80, 130);
        source.SetPixel(0, 0, selected); source.SetPixel(1, 0, Color.FromArgb(70, selected));
        source.SetPixel(2, 0, Color.FromArgb(0, selected)); source.SetPixel(3, 0, Color.FromArgb(30, 80, 131));
        int[] original = Pixels(source);
        var preview = new PixelPreview { Image = source, HighlightRgb = selected.ToArgb(), Width = 320, Height = 160, Zoom = 16 };
        var host = new Window { Content = preview, Width = 360, Height = 220, ShowActivated = false };
        try
        {
            host.Show(); Pump();
            object surface = typeof(PixelPreview).GetField("_surface", Private)!.GetValue(preview)!;
            var outline = (Geometry)surface.GetType().GetProperty("HighlightOutline")!.GetValue(surface)!;
            Require(outline.Bounds == new Rect(0, 0, 2, 1),
                $"Replacement outline is not the boundary of the exact visible color set: {outline.Bounds}");
            byte[] selectedPixels = RenderPreview();
            preview.HighlightRgb = null; Pump();
            Require(!selectedPixels.SequenceEqual(RenderPreview()), "Replacement outline was not rendered");
            preview.HighlightRgb = selected.ToArgb(); Pump();
            int firstPhase = (int)surface.GetType().GetProperty("HighlightPhase")!.GetValue(surface)!;
            var deadline = DateTime.UtcNow.AddSeconds(1);
            while ((int)surface.GetType().GetProperty("HighlightPhase")!.GetValue(surface)! == firstPhase &&
                   DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(10); }
            Require((int)surface.GetType().GetProperty("HighlightPhase")!.GetValue(surface)! != firstPhase,
                "Replacement outline did not animate");
            Capture(host, Path.Combine(directory, "exact-color-outline.png"));
            Rect bounds = (Rect)surface.GetType().GetProperty("ImageBounds")!.GetValue(surface)!;
            var element = (FrameworkElement)surface;
            Point location = element.TranslatePoint(new Point(bounds.X + bounds.Width * .875, bounds.Y + bounds.Height * .25), preview);
            Require(preview.TryGetImagePixel(location, out int x, out int y) && x == 3 && y == 0, "Zoomed eyedropper mapping selected the wrong pixel");
            Require(!preview.TryGetImagePixel(new Point(-1, -1), out _, out _), "Outside-preview eyedropper coordinates were accepted");
            Require(Pixels(source).SequenceEqual(original), "Preview/highlight mutated the bitmap");
            using var large = new Bitmap(200, 100);
            preview.Width = 320; preview.Height = 160; preview.Image = large; preview.Zoom = 8; Pump();
            preview.ScrollToRelativePosition(.5, .5); Pump(); Pump();
            Require(!preview.TryGetImagePixel(new Point(-10, -10), out _, out _),
                "Zoomed/scrolled preview accepts an off-viewport pointer and can erase invisible pixels");
            preview.Image = source; preview.Zoom = 16; Pump();
            Capture(host, Path.Combine(directory, "exact-color-highlight.png"));
            preview.ContinuousFit = true; preview.Zoom = 0; Pump();
            double firstWidth = (double)surface.GetType().GetProperty("ImageWidth")!.GetValue(surface)!;
            using var smaller = new Bitmap(2, 1); preview.Image = smaller; Pump();
            double secondWidth = (double)surface.GetType().GetProperty("ImageWidth")!.GetValue(surface)!;
            Require(Math.Abs(firstWidth - secondWidth) < .01, "Comparison fit depends on source pixel dimensions");
        }
        finally { host.Close(); }

        byte[] RenderPreview()
        {
            var frame = new RenderTargetBitmap((int)preview.ActualWidth, (int)preview.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            frame.Render(preview);
            byte[] pixels = new byte[(int)preview.ActualWidth * (int)preview.ActualHeight * 4];
            frame.CopyPixels(pixels, (int)preview.ActualWidth * 4, 0);
            return pixels;
        }
    }

    internal static string FindRepository()
    {
        for (var current = new DirectoryInfo(Environment.CurrentDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "Pixelizator.sln"))) return current.FullName;
        throw new Exception("Run feature checks from the intended repository checkout");
    }
    internal static T Get<T>(FrameworkElement owner, string name) => (T)(owner.FindName(name) ?? throw new Exception("Missing control: " + name));
    internal static object? Field(object owner, string name) => owner.GetType().GetField(name, Private)!.GetValue(owner);
    internal static object? Call(object owner, string name, params object?[] args)
    {
        try { return owner.GetType().GetMethod(name, Private)!.Invoke(owner, args); }
        catch (TargetInvocationException exception) { throw new Exception(name + ": " + (exception.InnerException?.ToString() ?? exception.ToString())); }
    }
    internal static int[] Pixels(Bitmap image)
    {
        var pixels = new int[image.Width * image.Height];
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++) pixels[y * image.Width + x] = image.GetPixel(x, y).ToArgb();
        return pixels;
    }
    internal static void Wait(Task task)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted) { Pump(); Thread.Sleep(5); if (timeout.Elapsed > TimeSpan.FromSeconds(45)) throw new Exception("Async feature operation timed out"); }
        task.GetAwaiter().GetResult(); Pump();
    }
    internal static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var item in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(item)) yield return descendant;
    }
    internal static void Capture(FrameworkElement element, string path)
    {
        Pump(); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)element.ActualWidth), Math.Max(1, (int)element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
    internal static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
