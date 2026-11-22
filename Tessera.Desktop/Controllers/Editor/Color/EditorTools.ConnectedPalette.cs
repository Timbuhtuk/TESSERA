using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Tessera.Core.Imaging;

namespace Tessera.Desktop;

internal sealed partial class EditorTools
{
    private readonly CheckBox connectedPaletteInput = new();

    private readonly CheckBox connectedTilesInput = new();

    private readonly IntegerInput connectedConnectivityInput = new() { Minimum = 0, Maximum = 100, Value = 75 };

    private readonly IntegerInput connectedTileSizeInput = new() { Minimum = 4, Maximum = 256, Value = 16 };

    private readonly IntegerInput connectedLocalInput = new() { Minimum = 0, Maximum = 100, Value = 55 };

    private readonly IntegerInput connectedDetailInput = new() { Minimum = 0, Maximum = 100, Value = 66 };

    private readonly IntegerInput connectedContextInput = new() { Minimum = 0, Maximum = 100, Value = 30 };

    private readonly ComboBox connectedSpaceInput = new();

    internal void InitializeConnectedPalette()
    {
        _editor.colorToolGroup.Header = Localization.Text("Palette");
        _editor.colorOnlyButton.Content = Localization.Text("CreateResult");
        var content = (StackPanel)_editor.colorToolGroup.Content;
        int position = content.Children.IndexOf(_editor.colorOnlyButton);
        connectedPaletteInput.Content = Localization.Text("UseColorConnectivity");
        connectedPaletteInput.Style = (Style)_editor.FindResource("Ts.SwitchBox");
        connectedTilesInput.Style = (Style)_editor.FindResource("Ts.SwitchBox");
        connectedPaletteInput.ToolTip = Localization.Text("ConsiderConnectedColorAreasNeighboringPixels");
        connectedPaletteInput.Margin = new Thickness(0, 12, 0, 5);
        AutomationProperties.SetName(connectedPaletteInput, Localization.Text("UseColorConnectivity"));
        content.Children.Insert(position++, connectedPaletteInput);

        var panel = new StackPanel();
        panel.Children.Add(Row("Connectivity", connectedConnectivityInput));
        connectedTilesInput.Content = new TextBlock { Text = Localization.Text("UseLocalRegions"),
            TextWrapping = TextWrapping.Wrap, MaxWidth = 180 };
        connectedTilesInput.IsChecked = true;
        panel.Children.Add(connectedTilesInput);
        panel.Children.Add(Row("RegionSizePx", connectedTileSizeInput));
        panel.Children.Add(Row("LocalWeight", connectedLocalInput));
        panel.Children.Add(Row("ColorDifference", connectedDetailInput));
        panel.Children.Add(Row("NeighborsDuringReplacement", connectedContextInput));
        connectedSpaceInput.Items.Add("Oklab"); connectedSpaceInput.Items.Add("CIE Lab");
        connectedSpaceInput.SelectedIndex = 0;
        panel.Children.Add(Row("ColorSpace", connectedSpaceInput));
        panel.Children.Add(new TextBlock
        {
            Text = Localization.Text("IfConnectedSelectionReceivesMoreThan"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4)
        });
        var details = new Expander
        {
            Header = Localization.Text("ConnectivitySettings"), Content = panel,
            Style = (Style)_editor.FindResource("ToolExpander"), Margin = new Thickness(0, 0, 0, 8), IsEnabled = false
        };
        content.Children.Insert(position, details);

        connectedPaletteInput.Checked += (_, _) => { details.IsEnabled = details.IsExpanded = true; Changed(); };
        connectedPaletteInput.Unchecked += (_, _) => { details.IsExpanded = details.IsEnabled = false; Changed(); };
        connectedTilesInput.Checked += (_, _) => Changed();
        connectedTilesInput.Unchecked += (_, _) => Changed();
        foreach (var input in new[] { connectedConnectivityInput, connectedTileSizeInput, connectedLocalInput,
            connectedDetailInput, connectedContextInput })
            input.ValueChanged += (_, _) => Changed();
        connectedSpaceInput.SelectionChanged += (_, _) => Changed();
        UpdateConnectedControls();

        void Changed()
        {
            UpdateConnectedControls();
            SettingsChanged(null, EventArgs.Empty);
            _editor.Preview.ScheduleLiveToolPreviews();
        }
        DockPanel Row(string label, FrameworkElement control)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = Localization.Text(label), Width = 150,
                VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            AutomationProperties.SetName(control, Localization.Text(label));
            row.Children.Add(control);
            return row;
        }
    }

    private void UpdateConnectedControls()
    {
        connectedTileSizeInput.IsEnabled = connectedLocalInput.IsEnabled = connectedTilesInput.IsChecked == true;
        _editor.paletteCountRow.IsEnabled = _editor.quantizationColorModeInput.IsChecked == true || connectedPaletteInput.IsChecked == true;
    }

    internal ConnectivityPaletteOptions? BuildConnectedPaletteOptions() => connectedPaletteInput.IsChecked == true ? new()
    {
        TargetColors = _editor.quantizationColorsInput.Value,
        ConnectivityWeight = connectedConnectivityInput.Value / 100.0,
        TileSize = connectedTilesInput.IsChecked == true ? connectedTileSizeInput.Value : 0,
        LocalWeight = connectedLocalInput.Value / 100.0,
        DetailWeight = connectedDetailInput.Value / 100.0,
        ContextWeight = connectedContextInput.Value / 100.0,
        DistanceSpace = connectedSpaceInput.SelectedIndex == 1 ? EPaletteDistanceSpace.CieLab : EPaletteDistanceSpace.Oklab
    } : null;
}
