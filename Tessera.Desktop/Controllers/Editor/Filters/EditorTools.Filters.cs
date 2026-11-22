using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using Microsoft.Win32;
using Tessera.Core.Imaging;
using Bitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;

namespace Tessera.Desktop;

internal sealed partial class EditorTools
{
    private bool _updatingAsciiRamp;

    internal (int Foreground, int Background) AsciiColors => (_asciiForegroundArgb, _asciiBackgroundArgb);

    private int _asciiForegroundArgb = DrawingColor.White.ToArgb();

    private int _asciiBackgroundArgb = DrawingColor.Black.ToArgb();

    internal void InitializeFilters()
    {
        _editor.monochromeButton.Click += (_, _) => ApplyMonochrome();
        _editor.sharpenButton.Click += (_, _) => ApplySharpen();
        _editor.sharpenDescription.Text = Localization.Text("StrengthensEdgesAfterDownscalingWithoutChanging");
        _editor.sharpenStrengthLabel.Text = Localization.Text("Strength");
        _editor.sharpenThresholdLabel.Text = Localization.Text("Threshold064");
        _editor.sharpenButton.Content = Localization.Text("SharpenImage");
        _editor.sharpenThresholdInput.ToolTip = Localization.Text("ColorChangesBelowThresholdAreLeft");
        AutomationProperties.SetName(_editor.sharpenStrengthInput, _editor.sharpenStrengthLabel.Text);
        AutomationProperties.SetName(_editor.sharpenThresholdInput, _editor.sharpenThresholdLabel.Text);
        _editor.sharpenStrengthInput.ValueChanged += (_, _) => { RefreshFilterState(); _editor.Preview.ScheduleLiveToolPreviews(); };
        _editor.sharpenThresholdInput.ValueChanged += (_, _) => _editor.Preview.ScheduleLiveToolPreviews();
        _editor.asciiSaveTextButton.Click += (_, _) => SaveAsciiText();
        _editor.asciiRenderButton.Click += (_, _) => RenderAsciiImage();
        _editor.asciiRampPresetInput.SelectionChanged += (_, _) => ApplyAsciiRampPreset();
        _editor.asciiCharactersInput.TextChanged += (_, _) => MarkAsciiRampCustom();
        _editor.asciiDescription.Text = Localization.Text("CellDensityAndShapeSelectCharacters");
        _editor.asciiColumnsLabel.Text = Localization.Text("Width0Auto");
        _editor.asciiColumnsInput.ToolTip = Localization.Text("CharactersPerLine");
        _editor.asciiColorModeLabel.Text = Localization.Text("CharacterColor");
        ((System.Windows.Controls.ComboBoxItem)_editor.asciiColorModeInput.Items[0]).Content = Localization.Text("Monochrome");
        ((System.Windows.Controls.ComboBoxItem)_editor.asciiColorModeInput.Items[1]).Content = Localization.Text("FromSource");
        ((System.Windows.Controls.ComboBoxItem)_editor.asciiColorModeInput.Items[2]).Content = Localization.Text("CustomColor");
        _editor.asciiColorModeInput.SelectionChanged += (_, _) => UpdateAsciiColorControls();
        _editor.asciiForegroundButton.Click += (_, _) => ChooseAsciiColor(foreground: true);
        _editor.asciiBackgroundButton.Click += (_, _) => ChooseAsciiColor(foreground: false);
        UpdateAsciiColorControls();
        _editor.asciiKeepSourceSizeInput.ToolTip = Localization.Text("PreservesSourceImageDimensions");
        _editor.asciiInvertSourceInput.ToolTip = Localization.Text("InvertsBrightnessBeforeSelectingCharacters");
        _editor.asciiInvertRenderInput.ToolTip = Localization.Text("SwapsCharacterAndBackgroundColorsIn");
        _editor.asciiDitherInput.Content = Localization.Text("OrderedDithering");
        _editor.asciiDitherInput.ToolTip = Localization.Text("MixesNeighboringGlyphDensitiesForSmoother");
        ApplyAsciiRampPreset();
        RefreshFilterState();
    }

    private void UpdateAsciiColorControls()
    {
        if (_editor.asciiColorModeInput is null) return;
        _editor.asciiForegroundButton.IsEnabled = _editor.asciiColorModeInput.SelectedIndex == (int)AsciiColorMode.Custom;
        _editor.asciiBackgroundButton.IsEnabled = _editor.asciiColorModeInput.SelectedIndex != (int)AsciiColorMode.Monochrome;
        _editor.asciiForegroundButton.Content = Localization.Text("CharacterColor") + ": " +
            PaletteColorEntry.FormatHex(DrawingColor.FromArgb(_asciiForegroundArgb));
        _editor.asciiBackgroundButton.Content = Localization.Text("BackgroundColor") + ": " +
            PaletteColorEntry.FormatHex(DrawingColor.FromArgb(_asciiBackgroundArgb));
    }

    private void ChooseAsciiColor(bool foreground)
    {
        int current = foreground ? _asciiForegroundArgb : _asciiBackgroundArgb;
        var picker = new ColorReplaceWindow(DrawingColor.FromArgb(current), Localization.Text(foreground ? "CharacterColor" : "BackgroundColor"))
        { Owner = Window.GetWindow(_editor.asciiToolGroup) ?? Host };
        picker.ShowDialog();
        if (picker.SelectedColor is not DrawingColor chosen) return;
        if (foreground) _asciiForegroundArgb = chosen.ToArgb(); else _asciiBackgroundArgb = chosen.ToArgb();
        UpdateAsciiColorControls();
        _editor.Preview.ScheduleLiveToolPreviews();
    }

    private void ApplyAsciiRampPreset()
    {
        int index = _editor.asciiRampPresetInput.SelectedIndex;
        if (_updatingAsciiRamp || index < 0 || index >= ImageFilters.AsciiRampPresets.Count) return;
        _updatingAsciiRamp = true;
        _editor.asciiCharactersInput.Text = ImageFilters.AsciiRampPresets[index].Characters;
        _updatingAsciiRamp = false;
    }

    private void MarkAsciiRampCustom()
    {
        if (_updatingAsciiRamp || _editor.asciiRampPresetInput.SelectedIndex == ImageFilters.AsciiRampPresets.Count) return;
        _updatingAsciiRamp = true;
        _editor.asciiRampPresetInput.SelectedIndex = ImageFilters.AsciiRampPresets.Count;
        _updatingAsciiRamp = false;
    }

    private Bitmap? GetFilterBasis()
        => _editor.ProcessingBasis == 1 && _editor._downscaledResult is not null ? _editor._downscaledResult : _editor._sourceImage;

    internal void RefreshFilterState()
    {
        Bitmap? basis = GetFilterBasis();
        bool enabled = !_editor._processing && basis is not null;
        _editor.monochromeButton.IsEnabled = enabled;
        _editor.sharpenButton.IsEnabled = enabled && _editor.sharpenStrengthInput.Value > 0;
        _editor.asciiSaveTextButton.IsEnabled = enabled;
        _editor.asciiRenderButton.IsEnabled = enabled;
        RefreshChannelAdjustmentState();
    }

    private async void ApplyMonochrome()
    {
        Bitmap? basis = GetFilterBasis();
        if (_editor._processing || basis is null || _editor._activeSource is null) return;
        bool useResult = ReferenceEquals(basis, _editor._downscaledResult);
        var document = _editor._activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        var settings = CaptureSettings();
        using var input = (Bitmap)basis.Clone();
        _editor.SetProcessingState(true);
        Host.statusLabel.Text = Localization.Text("ApplyingMonochromeAction");
        try
        {
            var generation = await Task.Run(() =>
            {
                using var output = ImageFilters.Monochrome(input, Host.CurrentProcessingProgress);
                return Host._library.SaveGeneration(document, output, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = Localization.Text("Monochrome"),
                    OperationId = ProcessingOperations.Monochrome,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = ProcessingOperations.Monochrome }),
                    ParentGenerationId = parentId,
                    PreservesTransparency = true,
                    Caption = Localization.Format("MonochromeFormat", output.Width, output.Height),
                    Log = Localization.Text("ColorsConvertedToGrayscale")
                });
            });
            if (!_editor.History.CommitGeneration(document, generation)) return;
            Host.Notify(Localization.Text("MonochromeSavedToHistory"), NotificationKind.Success);
        }
        catch (Exception ex)
        {
            Host._mainProgress?.Dispose();
            Host.Notify(Localization.Format("CouldNotApplyMonochromeFormat", ex.Message), NotificationKind.Error);
        }
        finally { _editor.SetProcessingState(false); }
    }

    private async void ApplySharpen()
    {
        Bitmap? basis = GetFilterBasis();
        if (_editor._processing || basis is null || _editor._activeSource is null || _editor.sharpenStrengthInput.Value == 0) return;
        int strength = _editor.sharpenStrengthInput.Value, threshold = _editor.sharpenThresholdInput.Value;
        bool useResult = ReferenceEquals(basis, _editor._downscaledResult);
        var document = _editor._activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        var settings = CaptureSettings();
        using var input = (Bitmap)basis.Clone();
        _editor.sharpenLivePreviewInput.IsChecked = false;
        _editor.SetProcessingState(true);
        Host.statusLabel.Text = Localization.Text("SharpeningAction");
        try
        {
            var generation = await Task.Run(() =>
            {
                using var output = ImageFilters.Sharpen(input, strength, threshold, progress: Host.CurrentProcessingProgress);
                return Host._library.SaveGeneration(document, output, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = Localization.Text("Sharpen"),
                    OperationId = ProcessingOperations.Sharpen,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep
                    {
                        OperationId = ProcessingOperations.Sharpen,
                        Sharpen = new SharpenSettings { Strength = strength, Threshold = threshold }
                    }),
                    ParentGenerationId = parentId,
                    PreservesTransparency = true,
                    Caption = Localization.Format("SharpenFormat", strength, output.Width, output.Height),
                    Log = Localization.Format("LocalSharpeningThresholdFormat", strength, threshold)
                });
            });
            if (!_editor.History.CommitGeneration(document, generation)) return;
            Host.Notify(Localization.Text("SharpenedResultSavedToHistory"), NotificationKind.Success);
        }
        catch (Exception ex)
        {
            Host._mainProgress?.Dispose();
            Host.Notify(Localization.Format("CouldNotSharpenTheImageFormat", ex.Message), NotificationKind.Error);
        }
        finally { _editor.SetProcessingState(false); }
    }

    private async void SaveAsciiText()
    {
        Bitmap? basis = GetFilterBasis();
        if (_editor._processing || basis is null) return;
        var dialog = new SaveFileDialog
        {
            Title = Localization.Text("SaveAsciiAsText"),
            Filter = Localization.Text("TextFileFileFilter"),
            DefaultExt = ".txt",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"{Path.GetFileNameWithoutExtension(_editor._activeSource?.Label ?? _editor._selectedFilePath) ?? "image"}_ascii.txt"
        };
        if (dialog.ShowDialog(Window.GetWindow(_editor.asciiSaveTextButton) ?? Host) != true) return;

        try { await SaveAsciiTextAsync(dialog.FileName); }
        catch (Exception ex) { Host.Notify(Localization.Format("CouldNotSaveAsciiTextFormat", ex.Message), NotificationKind.Error); }
    }

    private async Task SaveAsciiTextAsync(string path)
    {
        Bitmap? basis = GetFilterBasis();
        if (_editor._processing || basis is null) return;
        string output = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(output), ".txt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Localization.Text("SaveAsciiTextInATXT"));
        Host.EnsureExportPathAllowed(output);
        string characters = _editor.asciiCharactersInput.Text;
        bool keepSourceSize = _editor.asciiKeepSourceSizeInput.IsChecked == true;
        bool invertSource = _editor.asciiInvertSourceInput.IsChecked == true;
        bool dither = _editor.asciiDitherInput.IsChecked == true;
        int columns = _editor.asciiColumnsInput.Value;
        using var input = (Bitmap)basis.Clone();
        _editor.SetProcessingState(true);
        Host.statusLabel.Text = Localization.Text("SavingAsciiTextAction");
        try
        {
            string text = await Task.Run(() => columns > 0
                ? ImageFilters.ToAsciiAtColumns(input, keepSourceSize ? Math.Min(columns, input.Width) : columns,
                    characters, invertSource, dither, progress: Host.CurrentProcessingProgress)
                : keepSourceSize
                ? ImageFilters.ToAscii(input, characters, invertSource, dither, progress: Host.CurrentProcessingProgress)
                : ImageFilters.ToAscii(input, input.Width, characters, invertSource, dither, progress: Host.CurrentProcessingProgress));
            string temporary = Path.Combine(Path.GetDirectoryName(output)!, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(false));
                File.Move(temporary, output, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Host.CompleteProcessingProgress();
            Host.Notify(Localization.Format("AsciiTextSavedFormat", output), NotificationKind.Success);
        }
        finally { _editor.SetProcessingState(false); }
    }

    private async void RenderAsciiImage()
    {
        Bitmap? basis = GetFilterBasis();
        if (_editor._processing || basis is null || _editor._activeSource is null) return;
        string characters = _editor.asciiCharactersInput.Text;
        bool keepSourceSize = _editor.asciiKeepSourceSizeInput.IsChecked == true;
        bool invertSource = _editor.asciiInvertSourceInput.IsChecked == true;
        bool invertRender = _editor.asciiInvertRenderInput.IsChecked == true;
        bool dither = _editor.asciiDitherInput.IsChecked == true;
        AsciiColorMode colorMode = (AsciiColorMode)_editor.asciiColorModeInput.SelectedIndex;
        int foregroundArgb = _asciiForegroundArgb, backgroundArgb = _asciiBackgroundArgb;
        int columns = _editor.asciiColumnsInput.Value;
        bool useResult = ReferenceEquals(basis, _editor._downscaledResult);
        var document = _editor._activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        var settings = CaptureSettings();
        using var input = (Bitmap)basis.Clone();
        _editor.asciiLivePreviewInput.IsChecked = false;
        _editor.SetProcessingState(true);
        Host.statusLabel.Text = Localization.Text("RenderingAsciiImageAction");
        try
        {
            var created = await Task.Run(() =>
            {
                using var output = ImageFilters.RenderAscii(input, keepSourceSize, characters, invertSource, invertRender,
                    colorMode, foregroundArgb, backgroundArgb, columns, dither, Host.CurrentProcessingProgress, ImageFilters.DefaultAsciiFontSize);
                var generation = Host._library.SaveGeneration(document, output, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = "ASCII",
                    OperationId = ProcessingOperations.Ascii,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep
                    {
                        OperationId = ProcessingOperations.Ascii,
                        Ascii = new AsciiSettings
                        {
                            FontSize = ImageFilters.DefaultAsciiFontSize,
                            Characters = characters,
                            KeepSourceSize = keepSourceSize,
                            InvertSource = invertSource,
                            InvertColors = invertRender,
                            ColorMode = colorMode,
                            ForegroundArgb = foregroundArgb,
                            BackgroundArgb = backgroundArgb,
                            Columns = columns,
                            Dither = dither
                        }
                    }),
                    ParentGenerationId = parentId,
                    PreservesTransparency = false,
                    Caption = $"ASCII · {output.Width} × {output.Height}",
                    Log = Localization.Format("AsciiPxColorWidthInvertSourceFormat",
                        output.Width, output.Height, colorMode, columns == 0 ? Localization.Text("Auto") : columns.ToString(), invertSource, invertRender)
                });
                return generation;
            });
            if (!_editor.History.CommitGeneration(document, created)) return;
            Host.Notify(Localization.Text("AsciiImageSavedToHistory"), NotificationKind.Success);
        }
        catch (Exception ex)
        {
            Host._mainProgress?.Dispose();
            Host.Notify(Localization.Format("CouldNotRenderAsciiFormat", ex.Message), NotificationKind.Error);
        }
        finally { _editor.SetProcessingState(false); }
    }
}
