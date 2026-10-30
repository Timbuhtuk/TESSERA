using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using Microsoft.Win32;
using PixelArtDownscale;
using Bitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;

namespace DomainColorTest;

public partial class MainWindow
{
    private bool _updatingAsciiRamp;
    private int _asciiForegroundArgb = DrawingColor.White.ToArgb();
    private int _asciiBackgroundArgb = DrawingColor.Black.ToArgb();

    private void InitializeFilters()
    {
        monochromeButton.Click += (_, _) => ApplyMonochrome();
        sharpenButton.Click += (_, _) => ApplySharpen();
        sharpenTitle.Text = Localization.Text("Резкость");
        sharpenDescription.Text = Localization.Text("Усиливает границы после уменьшения без изменения размера и прозрачности. Потерянные детали не восстанавливаются.");
        sharpenStrengthLabel.Text = Localization.Text("Сила, %");
        sharpenThresholdLabel.Text = Localization.Text("Порог, 0–64");
        sharpenButton.Content = Localization.Text("Повысить резкость");
        sharpenThresholdInput.ToolTip = Localization.Text("Слабые перепады цвета ниже порога не усиливаются.");
        AutomationProperties.SetName(sharpenStrengthInput, sharpenStrengthLabel.Text);
        AutomationProperties.SetName(sharpenThresholdInput, sharpenThresholdLabel.Text);
        sharpenStrengthInput.ValueChanged += (_, _) => { RefreshFilterState(); ScheduleLiveToolPreviews(); };
        sharpenThresholdInput.ValueChanged += (_, _) => ScheduleLiveToolPreviews();
        asciiSaveTextButton.Click += (_, _) => SaveAsciiText();
        asciiRenderButton.Click += (_, _) => RenderAsciiImage();
        asciiRampPresetInput.SelectionChanged += (_, _) => ApplyAsciiRampPreset();
        asciiCharactersInput.TextChanged += (_, _) => MarkAsciiRampCustom();
        asciiDescription.Text = Localization.Text("Плотность и форма участков подбирают символы. PNG может использовать цвета исходника или выбранные цвета; TXT сохраняет только символы.");
        asciiColumnsLabel.Text = Localization.Text("Ширина, 0 — авто");
        asciiColumnsInput.ToolTip = Localization.Text("Количество символов в строке. 0 подбирает ширину автоматически; высота следует пропорциям исходника и символа.");
        asciiColorModeLabel.Text = Localization.Text("Цвет символов");
        ((System.Windows.Controls.ComboBoxItem)asciiColorModeInput.Items[0]).Content = Localization.Text("Монохром");
        ((System.Windows.Controls.ComboBoxItem)asciiColorModeInput.Items[1]).Content = Localization.Text("Из исходника");
        ((System.Windows.Controls.ComboBoxItem)asciiColorModeInput.Items[2]).Content = Localization.Text("Свой цвет");
        asciiColorModeInput.SelectionChanged += (_, _) => UpdateAsciiColorControls();
        asciiForegroundButton.Click += (_, _) => ChooseAsciiColor(foreground: true);
        asciiBackgroundButton.Click += (_, _) => ChooseAsciiColor(foreground: false);
        UpdateAsciiColorControls();
        asciiKeepSourceSizeInput.ToolTip = Localization.Text("Сохраняет размер исходника. При выключении строит изображение в размере символьной сетки с учётом прямоугольной формы символов.");
        asciiInvertSourceInput.ToolTip = Localization.Text("Инвертирует яркость перед подбором символов. Прозрачные участки остаются пустыми.");
        asciiInvertRenderInput.ToolTip = Localization.Text("Меняет местами цвет символов и фона в PNG. Для TXT не применяется.");
        asciiDitherInput.Content = Localization.Text("Упорядоченный дизеринг");
        asciiDitherInput.ToolTip = Localization.Text("Смешивает соседние символы разной плотности, чтобы плавнее передать полутона. Для TXT и PNG.");
        ApplyAsciiRampPreset();
        RefreshFilterState();
    }

    private void UpdateAsciiColorControls()
    {
        if (asciiColorModeInput is null) return;
        asciiForegroundButton.IsEnabled = asciiColorModeInput.SelectedIndex == (int)AsciiColorMode.Custom;
        asciiBackgroundButton.IsEnabled = asciiColorModeInput.SelectedIndex != (int)AsciiColorMode.Monochrome;
        asciiForegroundButton.Content = Localization.Text("Цвет символов") + ": " +
            PaletteColorEntry.FormatHex(DrawingColor.FromArgb(_asciiForegroundArgb));
        asciiBackgroundButton.Content = Localization.Text("Цвет фона") + ": " +
            PaletteColorEntry.FormatHex(DrawingColor.FromArgb(_asciiBackgroundArgb));
    }

    private void ChooseAsciiColor(bool foreground)
    {
        int current = foreground ? _asciiForegroundArgb : _asciiBackgroundArgb;
        var picker = new ColorReplaceWindow(DrawingColor.FromArgb(current), foreground ? "Цвет символов" : "Цвет фона")
        { Owner = Window.GetWindow(asciiToolGroup) ?? this };
        picker.ShowDialog();
        if (picker.SelectedColor is not DrawingColor chosen) return;
        if (foreground) _asciiForegroundArgb = chosen.ToArgb(); else _asciiBackgroundArgb = chosen.ToArgb();
        UpdateAsciiColorControls();
    }

    private void ApplyAsciiRampPreset()
    {
        int index = asciiRampPresetInput.SelectedIndex;
        if (_updatingAsciiRamp || index < 0 || index >= ImageFilters.AsciiRampPresets.Count) return;
        _updatingAsciiRamp = true;
        asciiCharactersInput.Text = ImageFilters.AsciiRampPresets[index].Characters;
        _updatingAsciiRamp = false;
    }

    private void MarkAsciiRampCustom()
    {
        if (_updatingAsciiRamp || asciiRampPresetInput.SelectedIndex == ImageFilters.AsciiRampPresets.Count) return;
        _updatingAsciiRamp = true;
        asciiRampPresetInput.SelectedIndex = ImageFilters.AsciiRampPresets.Count;
        _updatingAsciiRamp = false;
    }

    private Bitmap? GetFilterBasis()
        => ProcessingBasis == 1 && _downscaledResult is not null ? _downscaledResult : _sourceImage;

    private void RefreshFilterState()
    {
        Bitmap? basis = GetFilterBasis();
        bool enabled = !_processing && basis is not null;
        monochromeButton.IsEnabled = enabled;
        sharpenButton.IsEnabled = enabled && sharpenStrengthInput.Value > 0;
        asciiSaveTextButton.IsEnabled = enabled;
        asciiRenderButton.IsEnabled = enabled;
        RefreshChannelAdjustmentState();
    }

    private async void ApplyMonochrome()
    {
        Bitmap? basis = GetFilterBasis();
        if (_processing || basis is null || _activeSource is null) return;
        bool useResult = ReferenceEquals(basis, _downscaledResult);
        var document = _activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        var settings = CaptureSettings();
        using var input = (Bitmap)basis.Clone();
        SetProcessingState(true);
        statusLabel.Text = Localization.Text("Применение монохрома…");
        try
        {
            var generation = await Task.Run(() =>
            {
                using var output = ImageFilters.Monochrome(input);
                return _library.SaveGeneration(document, output, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = Localization.Text("Монохром"),
                    OperationId = ProcessingOperations.Monochrome,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = ProcessingOperations.Monochrome }),
                    ParentGenerationId = parentId,
                    PreservesTransparency = true,
                    Caption = Localization.Format("МОНОХРОМ · {0} × {1}", output.Width, output.Height),
                    Log = Localization.Text("Цвета переведены в оттенки серого. Размер и прозрачность сохранены.")
                });
            });
            if (!CommitGeneration(document, generation)) return;
            statusLabel.Text = Localization.Text("Монохром сохранён в истории.");
        }
        catch (Exception ex)
        {
            statusLabel.Text = Localization.Format("Не удалось применить монохром: {0}", ex.Message);
        }
        finally { SetProcessingState(false); }
    }

    private async void ApplySharpen()
    {
        Bitmap? basis = GetFilterBasis();
        if (_processing || basis is null || _activeSource is null || sharpenStrengthInput.Value == 0) return;
        int strength = sharpenStrengthInput.Value, threshold = sharpenThresholdInput.Value;
        bool useResult = ReferenceEquals(basis, _downscaledResult);
        var document = _activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        var settings = CaptureSettings();
        using var input = (Bitmap)basis.Clone();
        sharpenLivePreviewInput.IsChecked = false;
        SetProcessingState(true);
        statusLabel.Text = Localization.Text("Повышение резкости…");
        try
        {
            var generation = await Task.Run(() =>
            {
                using var output = ImageFilters.Sharpen(input, strength, threshold);
                return _library.SaveGeneration(document, output, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = Localization.Text("Резкость"),
                    OperationId = ProcessingOperations.Sharpen,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = ProcessingOperations.Sharpen,
                        Sharpen = new SharpenSettings { Strength = strength, Threshold = threshold } }),
                    ParentGenerationId = parentId,
                    PreservesTransparency = true,
                    Caption = Localization.Format("РЕЗКОСТЬ · {0}% · {1} × {2}", strength, output.Width, output.Height),
                    Log = Localization.Format("Локальная резкость {0}%, порог {1}. Размер и прозрачность сохранены.", strength, threshold)
                });
            });
            if (!CommitGeneration(document, generation)) return;
            statusLabel.Text = Localization.Text("Резкость сохранена в истории.");
        }
        catch (Exception ex)
        {
            statusLabel.Text = Localization.Format("Не удалось повысить резкость: {0}", ex.Message);
        }
        finally { SetProcessingState(false); }
    }

    private async void SaveAsciiText()
    {
        Bitmap? basis = GetFilterBasis();
        if (_processing || basis is null) return;
        var dialog = new SaveFileDialog
        {
            Title = Localization.Text("Сохранить ASCII как текст"),
            Filter = Localization.Text("Текстовый файл|*.txt"),
            DefaultExt = ".txt",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"{Path.GetFileNameWithoutExtension(_activeSource?.Label ?? _selectedFilePath) ?? "image"}_ascii.txt"
        };
        if (dialog.ShowDialog(Window.GetWindow(asciiSaveTextButton) ?? this) != true) return;

        try { await SaveAsciiTextAsync(dialog.FileName); }
        catch (Exception ex) { statusLabel.Text = Localization.Format("Не удалось сохранить ASCII-текст: {0}", ex.Message); }
    }

    private async Task SaveAsciiTextAsync(string path)
    {
        Bitmap? basis = GetFilterBasis();
        if (_processing || basis is null) return;
        string output = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(output), ".txt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Localization.Text("Сохраните ASCII-текст в файле TXT."));
        EnsureExportPathAllowed(output);
        string characters = asciiCharactersInput.Text;
        bool keepSourceSize = asciiKeepSourceSizeInput.IsChecked == true;
        bool invertSource = asciiInvertSourceInput.IsChecked == true;
        bool dither = asciiDitherInput.IsChecked == true;
        int columns = asciiColumnsInput.Value;
        using var input = (Bitmap)basis.Clone();
        SetProcessingState(true);
        statusLabel.Text = Localization.Text("Сохранение ASCII-текста…");
        try
        {
            string text = await Task.Run(() => columns > 0
                ? ImageFilters.ToAsciiAtColumns(input, keepSourceSize ? Math.Min(columns, input.Width) : columns,
                    characters, invertSource, dither)
                : keepSourceSize
                ? ImageFilters.ToAscii(input, characters, invertSource, dither)
                : ImageFilters.ToAscii(input, input.Width, characters, invertSource, dither));
            string temporary = Path.Combine(Path.GetDirectoryName(output)!, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(false));
                File.Move(temporary, output, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            statusLabel.Text = Localization.Format("ASCII-текст сохранён: {0}", output);
        }
        finally { SetProcessingState(false); }
    }

    private async void RenderAsciiImage()
    {
        Bitmap? basis = GetFilterBasis();
        if (_processing || basis is null || _activeSource is null) return;
        string characters = asciiCharactersInput.Text;
        bool keepSourceSize = asciiKeepSourceSizeInput.IsChecked == true;
        bool invertSource = asciiInvertSourceInput.IsChecked == true;
        bool invertRender = asciiInvertRenderInput.IsChecked == true;
        bool dither = asciiDitherInput.IsChecked == true;
        AsciiColorMode colorMode = (AsciiColorMode)asciiColorModeInput.SelectedIndex;
        int foregroundArgb = _asciiForegroundArgb, backgroundArgb = _asciiBackgroundArgb;
        int columns = asciiColumnsInput.Value;
        bool useResult = ReferenceEquals(basis, _downscaledResult);
        var document = _activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        var settings = CaptureSettings();
        using var input = (Bitmap)basis.Clone();
        SetProcessingState(true);
        statusLabel.Text = Localization.Text("Рендер ASCII-изображения…");
        try
        {
            var created = await Task.Run(() =>
            {
                using var output = ImageFilters.RenderAscii(input, keepSourceSize, characters, invertSource, invertRender,
                    colorMode, foregroundArgb, backgroundArgb, columns, dither);
                var generation = _library.SaveGeneration(document, output, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = "ASCII",
                    OperationId = ProcessingOperations.Ascii,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = ProcessingOperations.Ascii, Ascii = new AsciiSettings { Characters = characters, KeepSourceSize = keepSourceSize, InvertSource = invertSource, InvertColors = invertRender,
                        ColorMode = colorMode, ForegroundArgb = foregroundArgb, BackgroundArgb = backgroundArgb,
                        Columns = columns, Dither = dither } }),
                    ParentGenerationId = parentId,
                    PreservesTransparency = false,
                    Caption = $"ASCII · {output.Width} × {output.Height}",
                    Log = Localization.Format("ASCII: {0} × {1} px; цвет: {2}; ширина: {3}; инверсия исходника: {4}; инверсия рендера: {5}.",
                        output.Width, output.Height, colorMode, columns == 0 ? "авто" : columns.ToString(), invertSource, invertRender)
                });
                return generation;
            });
            if (!CommitGeneration(document, created)) return;
            statusLabel.Text = Localization.Text("ASCII-изображение сохранено в истории.");
        }
        catch (Exception ex)
        {
            statusLabel.Text = Localization.Format("Не удалось отрендерить ASCII: {0}", ex.Message);
        }
        finally { SetProcessingState(false); }
    }
}
