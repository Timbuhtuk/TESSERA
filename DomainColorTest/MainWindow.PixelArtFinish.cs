using System.Windows;
using Bitmap = System.Drawing.Bitmap;
using PixelArtDownscale;

namespace DomainColorTest;

public partial class MainWindow
{
    private async void FinishPixelArt(object? sender, RoutedEventArgs e)
    {
        if (_sourceImage is null || _activeSource is null || _processing) return;
        CommitInputs();

        bool useResult = operationSourceInput.SelectedIndex == 1 && _downscaledResult is not null;
        var basis = useResult ? _downscaledResult! : _sourceImage;
        var document = _activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        var settings = CaptureSettings();
        var options = new PixelArtFinishOptions
        {
            CellSize = finishCellInput.Value,
            SmoothPasses = finishSmoothInput.Value,
            ColorCount = finishColorsInput.Value,
            AdaptiveGrid = finishAdaptiveInput.IsChecked == true
        };

        using var source = (Bitmap)basis.Clone();
        SetProcessingState(true);
        statusLabel.Text = "Сборка пиксель-арта…";
        try
        {
            var generation = await Task.Run(() =>
            {
                using var result = PixelArtFinisher.Process(source, options);
                return _library.SaveGeneration(document, result, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = "Пиксель-арт",
                    ParentGenerationId = parentId,
                    PreservesTransparency = true,
                    CellSize = options.CellSize,
                    Caption = $"ПИКСЕЛЬ-АРТ · {result.Width} × {result.Height}",
                    Log = $"{source.Width} × {source.Height} → {result.Width} × {result.Height}. " +
                          $"Подготовка 3×3: {options.SmoothPasses} проходов; ячейка: {options.CellSize} px; " +
                          $"подстройка сетки: {(options.AdaptiveGrid ? "да" : "нет")}; " +
                          $"цветов: {(options.ColorCount == 0 ? "без ограничения" : options.ColorCount)}."
                });
            });
            document.Generations.Add(generation);
            SelectGeneration(generation);
            statusLabel.Text = $"Готово · {generation.Width} × {generation.Height}. Результат сохранён в истории.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Не удалось собрать пиксель-арт. Предыдущий результат доступен.";
            TesseraDialog.Show(this, ex.Message, "Ошибка обработки", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetProcessingState(false); }
    }
}
