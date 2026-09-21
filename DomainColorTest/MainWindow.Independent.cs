using System.Windows;
using Bitmap = System.Drawing.Bitmap;
using PixelArtDownscale;

namespace DomainColorTest;

public partial class MainWindow
{
    private void ScaleOnlyImage(object? sender, EventArgs e) => RunIndependentOperation(changeSize: true);
    private void ApplyColorsOnly(object? sender, EventArgs e) => RunIndependentOperation(changeSize: false);

    private async void RunIndependentOperation(bool changeSize)
    {
        if (_sourceImage is null || _activeSource is null || _processing) return;
        CommitInputs();
        var settings = CaptureSettings();
        DownscaleOptions options = settings.Downscale;
        bool useResult = operationSourceInput.SelectedIndex == 1 && _downscaledResult is not null;
        var basis = useResult ? _downscaledResult! : _sourceImage;
        if (changeSize && (options.TargetWidth > basis.Width || options.TargetHeight > basis.Height))
        {
            statusLabel.Text = "Размер результата не должен превышать размер выбранной основы.";
            return;
        }

        var document = _activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        using var source = (Bitmap)basis.Clone();
        SetProcessingState(true);
        statusLabel.Text = changeSize ? "Изменение размера…" : "Обработка цветов…";
        try
        {
            var generation = await Task.Run(() =>
            {
                using var result = changeSize
                    ? IndependentImageProcessor.Scale(source, options)
                    : IndependentImageProcessor.ApplyColors(source, options);
                string operation = changeSize ? "РАЗМЕР" : "ЦВЕТ";
                return _library.SaveGeneration(document, result, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = changeSize ? "Размер" : "Цвет",
                    ParentGenerationId = parentId,
                    PreservesTransparency = true,
                    Caption = $"{operation} · {result.Width} × {result.Height}",
                    Log = changeSize
                        ? $"Изменён размер {source.Width} × {source.Height} → {result.Width} × {result.Height}. Цвета не преобразованы."
                        : $"Обработаны цвета без изменения размера {result.Width} × {result.Height}."
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
