using System.Windows;
using Bitmap = System.Drawing.Bitmap;
using PixelArtDownscale;

namespace DomainColorTest;

public partial class MainWindow
{
    private enum IndependentOperation { Size, Colors, NeighborColors }

    private void ScaleOnlyImage(object? sender, EventArgs e) => RunIndependentOperation(IndependentOperation.Size);
    private void ApplyColorsOnly(object? sender, EventArgs e) => RunIndependentOperation(IndependentOperation.Colors);
    private void ApplyNeighborColorsOnly(object? sender, EventArgs e) => RunIndependentOperation(IndependentOperation.NeighborColors);

    private async void RunIndependentOperation(IndependentOperation operation)
    {
        if (_sourceImage is null || _activeSource is null || _processing) return;
        CommitInputs();
        var settings = CaptureSettings();
        DownscaleOptions options = settings.Downscale;
        bool useResult = operationSourceInput.SelectedIndex == 1 && _downscaledResult is not null;
        var basis = useResult ? _downscaledResult! : _sourceImage;
        bool changeSize = operation == IndependentOperation.Size;
        bool neighborColors = operation == IndependentOperation.NeighborColors;
        bool enlarged = changeSize && (options.TargetWidth > basis.Width || options.TargetHeight > basis.Height);

        var document = _activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        using var source = (Bitmap)basis.Clone();
        SetProcessingState(true);
        statusLabel.Text = operation switch
        {
            IndependentOperation.Size => enlarged ? "Увеличение изображения…" : "Изменение размера…",
            IndependentOperation.NeighborColors => "Локальная обработка цветов 3×3…",
            _ => "Обработка цветов…"
        };
        try
        {
            var generation = await Task.Run(() =>
            {
                using var result = operation switch
                {
                    IndependentOperation.Size => IndependentImageProcessor.Scale(source, options),
                    IndependentOperation.NeighborColors => IndependentImageProcessor.ApplyNeighborColors(source, options),
                    _ => IndependentImageProcessor.ApplyColors(source, options)
                };
                string colorOperation = options.IndependentColorMode switch
                {
                    IndependentColorMode.Quantization => "Квантование",
                    IndependentColorMode.Palette => "Палитра",
                    _ => "Цвет"
                };
                string label = changeSize ? enlarged ? "УВЕЛИЧЕНИЕ" : "РАЗМЕР" :
                    neighborColors ? "ЛОКАЛЬНЫЙ ЦВЕТ" : colorOperation.ToUpperInvariant();
                return _library.SaveGeneration(document, result, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = changeSize ? enlarged ? "Увеличение" : "Размер" :
                        neighborColors ? "Локальные цвета" : colorOperation,
                    ParentGenerationId = parentId,
                    PreservesTransparency = true,
                    Caption = $"{label} · {result.Width} × {result.Height}",
                    Log = changeSize
                        ? $"Изменён размер {source.Width} × {source.Height} → {result.Width} × {result.Height}. Цвета не преобразованы." + (enlarged ? " Увеличение без сглаживания." : "")
                        : neighborColors
                            ? $"Локальная обработка 3×3: проходов {options.LocalColorPasses}. Размер и прозрачность сохранены."
                            : $"{colorOperation}: размер {result.Width} × {result.Height} сохранён."
                });
            });
            document.Generations.Add(generation);
            SelectGeneration(generation);
            statusLabel.Text = $"Готово · {generation.Width} × {generation.Height}. Результат сохранён в истории.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Обработка или сохранение истории не завершены. Предыдущий результат доступен.";
            MessageBox.Show(this, ex.Message, "Ошибка обработки", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetProcessingState(false); }
    }
}
