using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using PixelArtDownscale;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class MainWindow
{
    private bool _updatingAsciiRamp;

    private void InitializeFilters()
    {
        monochromeButton.Click += (_, _) => ApplyMonochrome();
        asciiSaveTextButton.Click += (_, _) => SaveAsciiText();
        asciiRenderButton.Click += (_, _) => RenderAsciiImage();
        asciiRampPresetInput.SelectionChanged += (_, _) => ApplyAsciiRampPreset();
        asciiCharactersInput.TextChanged += (_, _) => MarkAsciiRampCustom();
        ApplyAsciiRampPreset();
        RefreshFilterState();
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
        => operationSourceInput.SelectedIndex == 1 && _downscaledResult is not null ? _downscaledResult : _sourceImage;

    private void RefreshFilterState()
    {
        Bitmap? basis = GetFilterBasis();
        bool enabled = !_processing && basis is not null;
        monochromeButton.IsEnabled = enabled;
        asciiSaveTextButton.IsEnabled = enabled;
        asciiRenderButton.IsEnabled = enabled;
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
        statusLabel.Text = "Применение монохрома…";
        try
        {
            var generation = await Task.Run(() =>
            {
                using var output = ImageFilters.Monochrome(input);
                return _library.SaveGeneration(document, output, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = "Монохром",
                    ParentGenerationId = parentId,
                    PreservesTransparency = true,
                    Caption = $"МОНОХРОМ · {output.Width} × {output.Height}",
                    Log = "Цвета переведены в оттенки серого. Размер и прозрачность сохранены."
                });
            });
            document.Generations.Add(generation);
            SelectGeneration(generation);
            statusLabel.Text = "Монохром сохранён в истории.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Не удалось применить монохром: {ex.Message}";
        }
        finally { SetProcessingState(false); }
    }

    private async void SaveAsciiText()
    {
        Bitmap? basis = GetFilterBasis();
        if (_processing || basis is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить ASCII как текст",
            Filter = "Текстовый файл|*.txt",
            DefaultExt = ".txt",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"{Path.GetFileNameWithoutExtension(_activeSource?.Label ?? _selectedFilePath) ?? "image"}_ascii.txt"
        };
        if (dialog.ShowDialog(Window.GetWindow(asciiSaveTextButton) ?? this) != true) return;

        string characters = asciiCharactersInput.Text;
        bool keepSourceSize = asciiKeepSourceSizeInput.IsChecked == true;
        bool invertSource = asciiInvertSourceInput.IsChecked == true;
        using var input = (Bitmap)basis.Clone();
        SetProcessingState(true);
        statusLabel.Text = "Сохранение ASCII-текста…";
        try
        {
            string text = await Task.Run(() => keepSourceSize
                ? ImageFilters.ToAscii(input, characters, invertSource)
                : ImageFilters.ToAsciiPerPixel(input, characters, invertSource));
            string output = Path.GetFullPath(dialog.FileName);
            string temporary = Path.Combine(Path.GetDirectoryName(output)!, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(false));
                File.Move(temporary, output, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            statusLabel.Text = $"ASCII-текст сохранён: {output}";
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Не удалось сохранить ASCII-текст: {ex.Message}";
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
        bool useResult = ReferenceEquals(basis, _downscaledResult);
        var document = _activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        var settings = CaptureSettings();
        using var input = (Bitmap)basis.Clone();
        SetProcessingState(true);
        statusLabel.Text = "Рендер ASCII-изображения…";
        try
        {
            var created = await Task.Run(() =>
            {
                string text = keepSourceSize
                    ? ImageFilters.ToAscii(input, characters, invertSource)
                    : ImageFilters.ToAsciiPerPixel(input, characters, invertSource);
                using var output = keepSourceSize
                    ? ImageFilters.RenderAscii(text, input.Width, input.Height, invertRender)
                    : ImageFilters.RenderAscii(text, ImageFilters.DefaultAsciiFontSize, invertRender);
                var generation = _library.SaveGeneration(document, output, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = "ASCII",
                    ParentGenerationId = parentId,
                    PreservesTransparency = false,
                    Caption = $"ASCII · {output.Width} × {output.Height}",
                    Log = keepSourceSize
                        ? $"ASCII автоматически рассчитан для {input.Width} × {input.Height} px. Размер исходника сохранён. Инверсия исходника: {invertSource}; инверсия рендера: {invertRender}."
                        : $"ASCII один пиксель в один символ: {input.Width} × {input.Height} символов. Итог {output.Width} × {output.Height} px. Инверсия исходника: {invertSource}; инверсия рендера: {invertRender}."
                });
                return generation;
            });
            document.Generations.Add(created);
            SelectGeneration(created);
            statusLabel.Text = "ASCII-изображение сохранено в истории.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Не удалось отрендерить ASCII: {ex.Message}";
        }
        finally { SetProcessingState(false); }
    }
}
