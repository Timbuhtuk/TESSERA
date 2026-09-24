using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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
            try { LibraryLocationChecks.Run(); EditorRegressionChecks.Run(); UpscaleUiChecks.Run(); WindowChromeChecks.Run(); Verify(); LibraryChecks.Run(); PixelWorkflowChecks.Run(); LibraryRemovalChecks.Run(); IconWorkspaceChecks.Run(); AsepriteUiChecks.Run(); BackgroundWorkspaceChecks.Run(); } catch (Exception e) { failure = e; }
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
        double minimumWidth = window.MinWidth;
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
            // WPF gives MinWidth precedence over the CI monitor's native size limit.
            // Native resizing and monitor bounds are covered by WindowChromeChecks.
            window.MinWidth = 1600; window.Width = 1600; Pump();
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
            window.MinWidth = minimumWidth;
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
            Require(colorTool is not null && colorTool != sizeTool &&
                !ReferenceEquals(Window.GetWindow(Get<Slider>("localBrightnessInput")), colorTool) &&
                colorTool.Title.StartsWith("Палитра"),
                "Palette tool did not open independently");
            Click("smoothingToolButton"); Pump();
            var smoothingTool = Window.GetWindow(Get<Slider>("localBrightnessInput"));
            Require(smoothingTool is not null && smoothingTool != colorTool &&
                smoothingTool.Title.StartsWith("Сглаживание") &&
                ReferenceEquals(Window.GetWindow(Get<Button>("neighborColorsButton")), smoothingTool),
                "Smoothing did not open as an independent action");
            Click("filtersToolButton"); Pump();
            var filtersTool = Window.GetWindow(Get<TextBox>("asciiCharactersInput"));
            Require(filtersTool is not null && filtersTool != smoothingTool &&
                filtersTool.Title.StartsWith("Фильтры") &&
                ReferenceEquals(Window.GetWindow(Get<Button>("monochromeButton")), filtersTool) &&
                ReferenceEquals(Window.GetWindow(Get<Button>("asciiSaveTextButton")), filtersTool) &&
                ReferenceEquals(Window.GetWindow(Get<Button>("asciiRenderButton")), filtersTool) &&
                Get<CheckBox>("asciiKeepSourceSizeInput").IsChecked == true &&
                Get<CheckBox>("asciiInvertSourceInput").IsChecked == false &&
                Get<CheckBox>("asciiInvertRenderInput").IsChecked == false &&
                Get<ComboBox>("asciiRampPresetInput").Items.Count == 5 &&
                Get<TextBox>("asciiCharactersInput").Text == ImageFilters.AsciiRampPresets[0].Characters,
                "Filters did not open with monochrome and ASCII actions");
            Get<ComboBox>("asciiRampPresetInput").SelectedIndex = 3; Pump();
            Require(Get<TextBox>("asciiCharactersInput").Text == ImageFilters.AsciiRampPresets[3].Characters,
                "ASCII ramp preset did not update its character list");
            Get<TextBox>("asciiCharactersInput").Text = "01"; Pump();
            Require(Get<ComboBox>("asciiRampPresetInput").SelectedIndex == 4,
                "Manual ASCII ramp did not switch to Custom");
            Click("profileToolButton"); Pump();
            var profileTool = Window.GetWindow(Get<TextBox>("processingLog"));
            Require(profileTool is not null && profileTool != colorTool &&
                ReferenceEquals(Window.GetWindow(Get<RadioButton>("sceneMode")), profileTool) &&
                ReferenceEquals(Window.GetWindow(action), profileTool), "Profile and processing tools were not grouped");
            CaptureTool(sizeTool!, "size-tool.png");
            CaptureTool(colorTool!, "color-tool.png");
            CaptureTool(smoothingTool!, "smoothing-tool.png");
            CaptureTool(filtersTool!, "filters-tool.png");
            CaptureTool(profileTool!, "mode-tool.png");
            foreach (var tool in new[] { gridTool, sizeTool, colorTool, smoothingTool, filtersTool, profileTool }) tool!.Close();
            Require(ReferenceEquals(Window.GetWindow(Get<IntegerInput>("widthInput")), window), "Tool settings were lost after closing their window");
            Get<IntegerInput>("widthInput").Value = 13;
            Get<IntegerInput>("heightInput").Value = 7;
            Get<ComboBox>("paletteInput").SelectedIndex = 3;
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
            window.MinWidth = 1600; window.Width = 1600; Pump();
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
            window.MinWidth = minimumWidth;
            window.Width = 1220; Pump();
            Capture("alignment.png");
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            Capture("minimum.png");
            Require(Get<StackPanel>("compactToolNavigation").Visibility == Visibility.Visible &&
                Get<StackPanel>("toolNavigation").Visibility == Visibility.Collapsed,
                "Narrow editor did not collapse tools into the processing menu");
            Click("toolsMenuButton");
            Require(Get<System.Windows.Controls.Primitives.Popup>("toolsMenuPopup").IsOpen,
                "Processing menu did not open on a narrow window");
            Click("compactSmoothingToolButton"); Pump();
            Require(Window.GetWindow(Get<Slider>("localBrightnessInput")) is { IsVisible: true },
                "Narrow processing menu did not open smoothing");
            Window.GetWindow(Get<Slider>("localBrightnessInput"))!.Close();
            Click("toolsMenuButton");
            Click("compactInfoToolButton"); Pump();
            Require(Window.GetWindow(Get<TextBlock>("infoSourceSummary")) is { IsVisible: true },
                "Narrow processing menu did not open Info");
            Window.GetWindow(Get<TextBlock>("infoSourceSummary"))!.Close();
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
            Require(Get<ComboBox>("paletteInput").Items.Count == 5 &&
                ((ComboBoxItem)Get<ComboBox>("paletteInput").Items[0]).Content?.ToString() == "DB16" &&
                ((DownscaleOptions)Invoke("BuildOptionsFromUi")!).Palette != PaletteKind.None,
                "Palette tool still offers a no-palette choice");
            Require(Get<RadioButton>("quantizationColorModeInput").IsChecked == true &&
                Get<StackPanel>("quantizationColorControls").IsEnabled &&
                !Get<StackPanel>("paletteColorControls").IsEnabled,
                "Color reduction did not default to quantization");
            Get<ComboBox>("paletteInput").SelectedIndex = 3;
            Get<RadioButton>("paletteColorModeInput").IsChecked = true;
            Require(!Get<StackPanel>("quantizationColorControls").IsEnabled &&
                Get<StackPanel>("paletteColorControls").IsEnabled &&
                ((DownscaleOptions)Invoke("BuildOptionsFromUi")!).IndependentColorMode == IndependentColorMode.Palette,
                "Palette mode did not disable quantization controls");
            Click("colorToolButton"); Pump();
            var activeColorTool = Window.GetWindow(Get<ComboBox>("paletteInput"));
            CaptureTool(activeColorTool!, "color-tool-palette.png");
            activeColorTool!.Close();
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
            Require(((ComboBoxItem)Get<ComboBox>("operationSourceInput").Items[1]).Content?.ToString()?.StartsWith("Результат №") == true,
                "Selected result was not named in the processing basis");
            Click("colorOnlyButton"); Wait();
            var colorGeneration = Get<SourceEntry>("_activeSource").SelectedGeneration!;
            var colorOnly = Get<Bitmap>("_downscaledResult");
            Require(colorGeneration.ParentGenerationId == sizeGeneration.Id &&
                colorGeneration.Settings.Downscale.IndependentColorMode == IndependentColorMode.Palette &&
                colorGeneration.Operation == "Палитра" &&
                colorOnly.Width == 15 && colorOnly.Height == 7,
                "Color operation did not use the selected result without resizing it");
            var paletteColors = Palettes.GetPalette(PaletteKind.GameBoy).ToHashSet();
            for (int y = 0; y < colorOnly.Height; y++) for (int x = 0; x < colorOnly.Width; x++)
                Require(paletteColors.Contains(colorOnly.GetPixel(x, y).ToArgb() & 0xFFFFFF), "Color operation ignored the palette");
            Get<ComboBox>("operationSourceInput").SelectedIndex = 0;
            Click("colorOnlyButton"); Wait();
            Require(Get<Bitmap>("_downscaledResult").Size == source.Size && Get<SourceEntry>("_activeSource").SelectedGeneration!.ParentGenerationId is null,
                "Color operation on the source unexpectedly scaled or chained a result");
            var paletteSourceGeneration = Get<SourceEntry>("_activeSource").SelectedGeneration!;
            Get<RadioButton>("quantizationColorModeInput").IsChecked = true;
            Require(Get<StackPanel>("quantizationColorControls").IsEnabled &&
                !Get<StackPanel>("paletteColorControls").IsEnabled &&
                ((DownscaleOptions)Invoke("BuildOptionsFromUi")!).IndependentColorMode == IndependentColorMode.Quantization,
                "Quantization mode did not disable palette controls");
            Get<IntegerInput>("quantizationColorsInput").Value = 1;
            Click("colorOnlyButton"); Wait();
            Require(Get<SourceEntry>("_activeSource").SelectedGeneration is { Operation: "Квантование" } quantizedGeneration &&
                quantizedGeneration.Settings.Downscale.IndependentColorMode == IndependentColorMode.Quantization,
                "Quantization choice was not saved with the result");
            Invoke("SelectGeneration", paletteSourceGeneration);
            Require(Get<RadioButton>("paletteColorModeInput").IsChecked == true &&
                Get<StackPanel>("paletteColorControls").IsEnabled,
                "History did not restore the selected color mode");
            Click("sizeToolButton"); Pump();
            Get<RadioButton>("manualInput").IsChecked = true;
            Get<Slider>("brightnessInput").Value = 73;
            Require(((DownscaleOptions)Invoke("BuildOptionsFromUi")!).ManualCriteria?.TargetBrightness == .73, "Manual criteria mapping failed");
            Get<IntegerInput>("localColorPassesInput").Value = 3;
            Get<Slider>("localBrightnessInput").Value = 20;
            Get<Slider>("localContrastInput").Value = 30;
            Get<Slider>("localSaturationInput").Value = 40;
            Get<Slider>("localEdgeInput").Value = 60;
            options = (DownscaleOptions)Invoke("BuildOptionsFromUi")!;
            Require(options.LocalColorPasses == 3 && options.ManualCriteria?.TargetBrightness == .73 &&
                options.LocalColorCriteria?.TargetBrightness == .2 &&
                options.LocalColorCriteria?.TargetContrast == .3 &&
                options.LocalColorCriteria?.TargetSaturation == .4 &&
                options.LocalColorCriteria?.TargetEdge == .6,
                "Local color sliders were not independent from size-tool criteria");
            Get<Slider>("brightnessInput").Value = 81;
            Require(Get<Slider>("localBrightnessInput").Value == 20,
                "Size-tool brightness changed the local 3×3 slider");
            Get<Slider>("brightnessInput").Value = 73;
            Get<ComboBox>("operationSourceInput").SelectedIndex = 0;
            Click("neighborColorsButton"); Wait();
            var neighborSourceGeneration = Get<SourceEntry>("_activeSource").SelectedGeneration!;
            var neighborSourceResult = Get<Bitmap>("_downscaledResult");
            Require(neighborSourceGeneration.ParentGenerationId is null &&
                neighborSourceGeneration.Settings.Downscale.LocalColorPasses == 3 &&
                neighborSourceGeneration.Settings.Downscale.ManualCriteria?.TargetBrightness == .73 &&
                neighborSourceGeneration.Settings.Downscale.LocalColorCriteria?.TargetBrightness == .2 &&
                neighborSourceResult.Size == source.Size,
                "Local colors on the source lost the basis, dimensions or manual settings");
            for (int y = 0; y < neighborSourceResult.Height; y++) for (int x = 0; x < neighborSourceResult.Width; x++)
                Require(sourceColors.Contains(neighborSourceResult.GetPixel(x, y).ToArgb()),
                    "Local colors introduced a color absent from the source");
            Get<IntegerInput>("localColorPassesInput").Value = 5;
            Get<ComboBox>("operationSourceInput").SelectedIndex = 1;
            Click("neighborColorsButton"); Wait();
            var neighborResultGeneration = Get<SourceEntry>("_activeSource").SelectedGeneration!;
            Require(neighborResultGeneration.ParentGenerationId == neighborSourceGeneration.Id &&
                neighborResultGeneration.Settings.Downscale.LocalColorPasses == 5,
                "Local colors did not chain from the selected result");
            Get<Slider>("localBrightnessInput").Value = 68;
            Require(Get<Slider>("brightnessInput").Value == 73,
                "Local 3×3 brightness changed the size-tool slider");
            Invoke("SelectGeneration", neighborSourceGeneration);
            Require(Get<IntegerInput>("localColorPassesInput").Value == 3 &&
                Get<Slider>("localBrightnessInput").Value == 20 &&
                Get<Slider>("localContrastInput").Value == 30 &&
                Get<Slider>("localSaturationInput").Value == 40 &&
                Get<Slider>("localEdgeInput").Value == 60,
                "History did not restore the independent local color settings");
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
            options = Get<SourceEntry>("_activeSource").SelectedGeneration!.Settings.Downscale;
            Require(options.SpriteMode && options.Quantization == QuantizationMethod.MedianCut &&
                options.Palette == PaletteKind.None && options.AlphaThreshold == 75 &&
                options.QuantizationColors == 64 && !options.UseColorWeights &&
                options.TargetWidth == 15 && options.TargetHeight == 7,
                "Processing did not apply the details profile");
            Require(((DownscaleOptions)Invoke("BuildOptionsFromUi")!).Palette == PaletteKind.DB16,
                "Restoring Details exposed an unavailable no-palette option");
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
            Click("infoToolButton"); Pump();
            var infoTool = Window.GetWindow(Get<TextBlock>("infoSourceSummary"));
            Require(infoTool is { IsVisible: true }, "Info did not open a separate window");
            var infoUntil = DateTime.UtcNow.AddSeconds(15);
            while (Get<TextBlock>("infoPaletteStatus").Text.Contains("Подсчёт") && DateTime.UtcNow < infoUntil)
            {
                Pump(); Thread.Sleep(5);
            }
            Require(Get<TextBlock>("infoSourceSummary").Text.Contains($"{source.Width} × {source.Height}") &&
                Get<TextBlock>("infoSourceSummary").Text.Contains($"{ImageColorTools.CountColors(source):N0}") &&
                Get<ItemsControl>("infoPaletteList").Items.Count == ImageColorTools.CountColors(source),
                "Info did not count and list visible source colors");
            Require(Get<TextBlock>("infoResultSummary").Text == "Пока нет результата",
                "Info retained an old result after switching source");
            Color oldColor = source.GetPixel(0, 0);
            var colorEntry = Get<ItemsControl>("infoPaletteList").Items.Cast<PaletteColorEntry>()
                .Single(color => color.Color.ToArgb() == Color.FromArgb(oldColor.R, oldColor.G, oldColor.B).ToArgb());
            Invoke("InfoColorClick", new Button { DataContext = colorEntry }, new RoutedEventArgs(Button.ClickEvent));
            var replaceTool = infoTool!.OwnedWindows.OfType<ColorReplaceWindow>().Single(tool => tool.IsVisible);
            var selectedColor = (PaletteColorEntry)((ListBox)replaceTool.FindName("paletteList")).SelectedItem;
            Require(selectedColor.Hex == colorEntry.Hex &&
                ((TextBox)replaceTool.FindName("newHexInput")).Text == colorEntry.Hex &&
                ((TextBox)replaceTool.FindName("oldHexInput")).IsReadOnly,
                "Replacement palette did not preselect the clicked color");
            double initialHue = (double)typeof(ColorReplaceWindow).GetField("_hue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(replaceTool)!;
            Require(Math.Abs(initialHue - oldColor.GetHue()) < 0.1 &&
                replaceTool.FindName("colorField") is FrameworkElement &&
                replaceTool.FindName("hueStrip") is FrameworkElement,
                "Color picker did not start at the selected image color");
            typeof(ColorReplaceWindow).GetField("_hue", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(replaceTool, 240.0);
            typeof(ColorReplaceWindow).GetField("_saturation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(replaceTool, 1.0);
            typeof(ColorReplaceWindow).GetField("_value", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(replaceTool, 1.0);
            typeof(ColorReplaceWindow).GetMethod("UpdateFromPicker", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(replaceTool, null);
            Require(((TextBox)replaceTool.FindName("newHexInput")).Text == "#0000FF",
                "Color picker did not update the replacement HEX value");
            ((TextBox)replaceTool.FindName("newHexInput")).Text = "#01FE03";
            double enteredHue = (double)typeof(ColorReplaceWindow).GetField("_hue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(replaceTool)!;
            Require(Math.Abs(enteredHue - Color.FromArgb(1, 254, 3).GetHue()) < 0.1,
                "Entering HEX did not update the color picker");
            Require(((Button)replaceTool.FindName("replaceButton")).IsEnabled,
                "Valid color replacement remained disabled");
            CaptureTool(infoTool, "info-tool.png");
            CaptureTool(replaceTool, "color-replace.png");
            ((Button)replaceTool.FindName("replaceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait();
            var replaced = Get<Bitmap>("_downscaledResult");
            Require(Get<SourceEntry>("_activeSource").SelectedGeneration?.Operation == "Замена цвета" &&
                replaced.GetPixel(0, 0).ToArgb() == Color.FromArgb(oldColor.A, 1, 254, 3).ToArgb() &&
                Get<Bitmap>("_sourceImage").GetPixel(0, 0).ToArgb() == oldColor.ToArgb(),
                "Info color replacement damaged source or missed result history");
            Get<ComboBox>("operationSourceInput").SelectedIndex = 1;
            infoUntil = DateTime.UtcNow.AddSeconds(15);
            while (Get<TextBlock>("infoPaletteStatus").Text.Contains("Подсчёт") && DateTime.UtcNow < infoUntil)
            {
                Pump(); Thread.Sleep(5);
            }
            Require(Get<TextBlock>("infoBasisLabel").Text.Contains("результата") &&
                Get<TextBlock>("infoResultSummary").Text.Contains($"{replaced.Width} × {replaced.Height}"),
                "Info did not follow the selected result");
            infoTool!.Close();
            using (var manyColors = new Bitmap(33, 32))
            {
                for (int q = 0; q < 1056; q++)
                    manyColors.SetPixel(q % 33, q / 33, Color.FromArgb((q >> 16) & 255, (q >> 8) & 255, q & 255));
                string manyPath = Path.Combine(directory, "many-colors.png");
                manyColors.Save(manyPath);
                Invoke("LoadImage", manyPath);
            }
            Click("infoToolButton"); Pump();
            infoTool = Window.GetWindow(Get<TextBlock>("infoSourceSummary"));
            infoUntil = DateTime.UtcNow.AddSeconds(15);
            while (Get<TextBlock>("infoPaletteStatus").Text.Contains("Подсчёт") && DateTime.UtcNow < infoUntil)
            {
                Pump(); Thread.Sleep(5);
            }
            Require(Get<TextBlock>("infoPaletteStatus").Text.Contains("Список доступен до 1024") &&
                Get<ItemsControl>("infoPaletteList").Items.Count == 0,
                "Info attempted to display a palette above its 1024-color limit");
            Require(Get<Button>("infoManualReplaceButton").Visibility == Visibility.Visible,
                "Info did not offer HEX replacement for a large palette");
            Click("infoManualReplaceButton"); Pump();
            replaceTool = infoTool!.OwnedWindows.OfType<ColorReplaceWindow>().Single(tool => tool.IsVisible);
            Require(!((TextBox)replaceTool.FindName("oldHexInput")).IsReadOnly &&
                ((FrameworkElement)replaceTool.FindName("paletteSection")).Visibility == Visibility.Collapsed,
                "Large-palette replacement did not open in manual HEX mode");
            ((TextBox)replaceTool.FindName("oldHexInput")).Text = "#000000";
            ((TextBox)replaceTool.FindName("newHexInput")).Text = "#FFFFFF";
            Require(((Button)replaceTool.FindName("replaceButton")).IsEnabled,
                "Info disabled HEX replacement for a large palette");
            ((Button)replaceTool.FindName("replaceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait();
            Require(Get<Bitmap>("_downscaledResult").GetPixel(0, 0).ToArgb() == Color.White.ToArgb() &&
                Get<Bitmap>("_sourceImage").GetPixel(0, 0).ToArgb() == Color.Black.ToArgb(),
                "HEX replacement failed when the full palette was unavailable");
            infoTool!.Close();
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
    private static void EqualPixels(Bitmap actual, Bitmap expected, string context)
    {
        Require(actual.Size == expected.Size, context + ": dimensions");
        for (int y = 0; y < actual.Height; y++)
            for (int x = 0; x < actual.Width; x++)
                Require(actual.GetPixel(x, y).ToArgb() == expected.GetPixel(x, y).ToArgb(), context + $": pixel {x},{y}");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
