using System.Windows.Controls;

namespace Tessera.Desktop;

internal sealed partial class EditorTools
{
    private PalettePreviewTooltips? _palettePreviews;

    internal void InitializePalettePreviews()
    {
        _palettePreviews = new PalettePreviewTooltips();
        _palettePreviews.Attach(_editor.paletteInput, index => new ColorPreviewSettings(
            (EPaletteKind)(index + 1), _editor.paletteStepInput.Value, _editor.ditheringInput.IsChecked == true));
        _palettePreviews.Attach(_editor.quantizationInput, index => new ColorPreviewSettings(
            EPaletteKind.None, Quantization: (EQuantizationMethod)index,
            ColorCount: _editor.quantizationColorsInput.Value, UseColorWeights: _editor.colorWeightsInput.IsChecked == true));
        _editor.paletteStepInput.ValueChanged += (_, _) => _palettePreviews.RefreshOpenTooltips();
        _editor.quantizationColorsInput.ValueChanged += (_, _) => _palettePreviews.RefreshOpenTooltips();
        foreach (var check in new[] { _editor.ditheringInput, _editor.colorWeightsInput })
        {
            check.Checked += (_, _) => _palettePreviews.RefreshOpenTooltips();
            check.Unchecked += (_, _) => _palettePreviews.RefreshOpenTooltips();
        }
        Host.Closed += (_, _) => _palettePreviews.Close();
    }
}
