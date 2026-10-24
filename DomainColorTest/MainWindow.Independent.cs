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

    private void InitializeCleanupControls()
    {
        var defaults = new ColorCleanupOptions();
        localBrightnessInput.Value = defaults.ColorTolerance / .4;
        localContrastInput.Value = defaults.Strength * 100;
        localSaturationInput.Value = defaults.DetailProtection * 100;
        localEdgeInput.Value = defaults.EdgeProtection * 100;
        cleanupDescription.Text = Localization.Text("Объединяет близкие оттенки по соседним пикселям, сохраняя линии, границы и прозрачность. Новые цвета не создаются.");
        neighborColorsButton.Content = Localization.Text("Очистить цвета");
        neighborColorsButton.ToolTip = Localization.Text("Убирает цветовой шум и объединяет близкие оттенки без изменения размера и прозрачности.");
        localColorPassesInput.ToolTip = Localization.Text("Повторяет очистку до указанного числа проходов. Обработка останавливается раньше, если цвета перестали меняться.");
        localBrightnessInput.ToolTip = Localization.Text("Максимальная разница близких оттенков. Больше значение — больше цветов объединяется; 0 сохраняет исходник.");
        localContrastInput.ToolTip = Localization.Text("Насколько сильно соседние пиксели влияют на выбор цвета. При 0 очистка выключена.");
        localSaturationInput.ToolTip = Localization.Text("Сохраняет тонкие линии и их окончания. Уменьшите, чтобы удалять больше мелкого цветового шума.");
        localEdgeInput.ToolTip = Localization.Text("Защищает границы устойчивых цветовых областей от размывания близкими оттенками.");
        System.Windows.Automation.AutomationProperties.SetName(localBrightnessInput, Localization.Text("Сходство цветов"));
        System.Windows.Automation.AutomationProperties.SetName(localContrastInput, Localization.Text("Сила очистки"));
        System.Windows.Automation.AutomationProperties.SetName(localSaturationInput, Localization.Text("Защита деталей"));
        System.Windows.Automation.AutomationProperties.SetName(localEdgeInput, Localization.Text("Защита границ"));
        UpdateDependentControls();
    }

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
            IndependentOperation.NeighborColors => Localization.Text("Очистка близких оттенков с защитой деталей…"),
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
                    neighborColors ? Localization.Text("ОЧИСТКА ЦВЕТОВ") : colorOperation.ToUpperInvariant();
                return _library.SaveGeneration(document, result, null, new GenerationEntry
                {
                    Settings = settings,
                    OperationId = changeSize ? ProcessingOperations.Resize : neighborColors ? ProcessingOperations.Cleanup : ProcessingOperations.Colors,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = changeSize ? ProcessingOperations.Resize : neighborColors ? ProcessingOperations.Cleanup : ProcessingOperations.Colors, Downscale = options }),
                    Operation = changeSize ? enlarged ? Localization.Text("Увеличение") : Localization.Text("Размер") :
                        neighborColors ? Localization.Text("Очистка цветов") : colorOperation,
                    ParentGenerationId = parentId,
                    PreservesTransparency = true,
                    Caption = $"{label} · {result.Width} × {result.Height}",
                    Log = changeSize
                        ? Localization.Format("Изменён размер {0} × {1} → {2} × {3}. Цвета не преобразованы.", source.Width, source.Height, result.Width, result.Height) + (enlarged ? Localization.Text(" Увеличение без сглаживания.") : "")
                        : neighborColors
                            ? Localization.Format("Очистка близких оттенков: {0:0.#} ΔE; сила {1:0}%; защита деталей {2:0}%; защита границ {3:0}%; проходов {4}. Размер и прозрачность сохранены.",
                                options.Cleanup!.ColorTolerance, options.Cleanup.Strength * 100, options.Cleanup.DetailProtection * 100,
                                options.Cleanup.EdgeProtection * 100, options.LocalColorPasses)
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
