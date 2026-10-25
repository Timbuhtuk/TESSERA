using System.Windows;
using PixelArtDownscale;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class MainWindow
{
    private async void TrimTransparentEdges(object? sender, RoutedEventArgs e)
    {
        var basis = ProcessingBasis == 1 && _downscaledResult is not null
            ? _downscaledResult : _sourceImage;
        if (_processing || basis is null || _activeSource is null) return;

        var document = _activeSource;
        Guid? parentId = ReferenceEquals(basis, _downscaledResult) ? document.SelectedGeneration?.Id : null;
        var settings = CaptureSettings();
        using var input = (Bitmap)basis.Clone();
        SetProcessingState(true);
        statusLabel.Text = Localization.Text("Обрезка прозрачных краёв…");
        try
        {
            var generation = await Task.Run(() =>
            {
                using var output = BitmapFactory.CropTransparentEdges(input);
                return _library.SaveGeneration(document, output, null, new GenerationEntry
                {
                    Settings = settings,
                    OperationId = ProcessingOperations.Trim,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = ProcessingOperations.Trim }),
                    Operation = Localization.Text("Обрезка прозрачных краёв"),
                    ParentGenerationId = parentId,
                    PreservesTransparency = System.Drawing.Image.IsAlphaPixelFormat(output.PixelFormat),
                    Caption = Localization.Format("ОБРЕЗКА · {0} × {1}", output.Width, output.Height),
                    Log = Localization.Format("Прозрачные края обрезаны: {0} × {1} → {2} × {3}. Учтены пиксели с альфой от 1 до 255.", input.Width, input.Height, output.Width, output.Height)
                });
            });
            document.Generations.Add(generation);
            SelectGeneration(generation);
            statusLabel.Text = Localization.Format("Обрезка сохранена в истории · {0} × {1}.", generation.Width, generation.Height);
        }
        catch (Exception ex)
        {
            statusLabel.Text = Localization.Text("Не удалось обрезать прозрачные края.");
            TesseraDialog.Show(this, ex.Message, Localization.Text("Ошибка обрезки"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetProcessingState(false); }
    }
}
