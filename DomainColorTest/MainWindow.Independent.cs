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
        bool useResult = ProcessingBasis == 1 && _downscaledResult is not null;
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
            IndependentOperation.Size => enlarged ? Localization.Text("Увеличение изображения…") : Localization.Text("Изменение размера…"),
            IndependentOperation.NeighborColors => Localization.Text("Локальная обработка цветов 3×3…"),
            _ => Localization.Text("Обработка цветов…")
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
                    IndependentColorMode.Quantization => Localization.Text("Квантование"),
                    IndependentColorMode.Palette => Localization.Text("Палитра"),
                    _ => Localization.Text("Цвет")
                };
                string label = changeSize ? enlarged ? Localization.Text("УВЕЛИЧЕНИЕ") : Localization.Text("РАЗМЕР") :
                    neighborColors ? Localization.Text("ЛОКАЛЬНЫЙ ЦВЕТ") : colorOperation.ToUpperInvariant();
                return _library.SaveGeneration(document, result, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = changeSize ? enlarged ? Localization.Text("Увеличение") : Localization.Text("Размер") :
                        neighborColors ? Localization.Text("Локальные цвета") : colorOperation,
                    ParentGenerationId = parentId,
                    PreservesTransparency = true,
                    Caption = $"{label} · {result.Width} × {result.Height}",
                    Log = changeSize
                        ? Localization.Format("Изменён размер {0} × {1} → {2} × {3}. Цвета не преобразованы.", source.Width, source.Height, result.Width, result.Height) + (enlarged ? Localization.Text(" Увеличение без сглаживания.") : "")
                        : neighborColors
                            ? Localization.Format("Локальная обработка 3×3: проходов {0}. Размер и прозрачность сохранены.", options.LocalColorPasses)
                            : Localization.Format("{0}: размер {1} × {2} сохранён.", colorOperation, result.Width, result.Height)
                });
            });
            document.Generations.Add(generation);
            SelectGeneration(generation);
            statusLabel.Text = Localization.Format("Готово · {0} × {1}. Результат сохранён в истории.", generation.Width, generation.Height);
        }
        catch (Exception ex)
        {
            statusLabel.Text = Localization.Text("Обработка или сохранение истории не завершены. Предыдущий результат доступен.");
            TesseraDialog.Show(this, ex.Message, Localization.Text("Ошибка обработки"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetProcessingState(false); }
    }
}
