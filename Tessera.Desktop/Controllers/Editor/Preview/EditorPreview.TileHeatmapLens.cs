using System.Windows;
using System.Windows.Controls;

namespace Tessera.Desktop;

internal sealed partial class EditorPreview
{
    private readonly GroupBox _tileHeatmapLensGroup = new();

    internal GroupBox TileHeatmapLensGroup => _tileHeatmapLensGroup;

    private readonly LensGridInputs _tileHeatmapGrid = new();

    private readonly Slider _tileHeatmapThreshold = new() { Minimum = 0, Maximum = 30, Value = 5, TickFrequency = .5, IsSnapToTickEnabled = true };

    private readonly Slider _tileHeatmapOpacity = new() { Minimum = 0, Maximum = 100, Value = 70 };

    private readonly Slider _tileHeatmapStripe = new() { Minimum = 1, Maximum = 8, Value = 1, IsSnapToTickEnabled = true };

    private readonly CheckBox _tileHeatmapX = new() { IsChecked = true };

    private readonly CheckBox _tileHeatmapY = new() { IsChecked = true };

    internal void InitializeTileHeatmapLens()
    {
        _tileHeatmapLensGroup.Header = Localization.Text("EdgeHeatmap");
        var heatmap = new StackPanel();
        _tileHeatmapGrid.AddRows(heatmap, UpdateTileHeatmapLens);
        _tileHeatmapX.Content = Localization.Text("LeftAndRightSeam");
        _tileHeatmapY.Content = Localization.Text("TopAndBottomSeam");
        foreach (var input in new[] { _tileHeatmapX, _tileHeatmapY })
        {
            input.Style = (Style)_editor.FindResource("Ts.SwitchBox");
            input.Margin = new Thickness(0, 0, 0, 8);
            input.Checked += (_, _) => UpdateTileHeatmapLens();
            input.Unchecked += (_, _) => UpdateTileHeatmapLens();
            heatmap.Children.Add(input);
        }
        heatmap.Children.Add(LensSlider("ColorDifferenceThreshold", "DifferenceThresholdFormat", _tileHeatmapThreshold));
        heatmap.Children.Add(LensSlider("HeatmapOpacity", "OpacityFormat", _tileHeatmapOpacity));
        heatmap.Children.Add(LensSlider("HeatmapStripWidth", "HeatmapStripWidthPxFormat", _tileHeatmapStripe));
        heatmap.Children.Add(new TextBlock
        {
            Text = Localization.Text("GreenThresholdRed4Threshold"),
            TextWrapping = TextWrapping.Wrap
        });
        foreach (var slider in new[] { _tileHeatmapThreshold, _tileHeatmapOpacity, _tileHeatmapStripe })
            slider.ValueChanged += (_, _) => UpdateTileHeatmapLens();
        _tileHeatmapLensGroup.Content = heatmap;

        _editor.settingsPanel.Children.Add(_tileHeatmapLensGroup);
    }

    internal void UpdateTileHeatmapLens()
    {
        PreviewTileHeatmap? heatmap = IsLensVisible("tileHeatmapLens", IsLensOpen("tileHeatmapLens"))
            ? new PreviewTileHeatmap(_tileHeatmapGrid.Grid, _tileHeatmapX.IsChecked == true, _tileHeatmapY.IsChecked == true,
                _tileHeatmapThreshold.Value, _tileHeatmapOpacity.Value / 100, _tileHeatmapStripe.Value)
            : null;
        _editor.sourcePreview.TileHeatmap = _editor.resultPreview.TileHeatmap = heatmap;
    }
}
