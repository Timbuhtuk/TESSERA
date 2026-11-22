using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using DrawingColor = System.Drawing.Color;

namespace Tessera.Desktop;

internal sealed partial class EditorPreview
{
    private readonly GroupBox _gridLensGroup = new();

    internal GroupBox GridLensGroup => _gridLensGroup;

    private readonly CheckBox _gridLensVisible = new() { IsChecked = true };

    private readonly CheckBox _gridLensDashed = new();

    private readonly Slider _gridLensOpacity = new() { Minimum = 0, Maximum = 100, Value = 63 };

    private readonly LensGridInputs _gridLensInputs = new();

    private readonly Button _gridLensColorButton = new();

    private DrawingColor _gridLensColor = DrawingColor.FromArgb(0, 200, 255);

    internal void InitializeGridLens()
    {
        _gridLensGroup.Header = Localization.Text("LensGrid");
        var content = new StackPanel();
        _gridLensVisible.Content = Localization.Text("ShowGrid");
        _gridLensVisible.Style = (Style)_editor.FindResource("Ts.SwitchBox");
        _gridLensVisible.ToolTip = Localization.Text("ShowsGridLinesOnlyRegularPreview");
        _gridLensVisible.Margin = new Thickness(0, 0, 0, 8);
        content.Children.Add(_gridLensVisible);
        _gridLensInputs.AddRows(content, UpdateGridLens);
        _gridLensColorButton.HorizontalContentAlignment = HorizontalAlignment.Left;
        _gridLensColorButton.MinHeight = 30;
        content.Children.Add(GridLensRow("GridColor", _gridLensColorButton));
        content.Children.Add(LensSlider("GridOpacity", "OpacityFormat", _gridLensOpacity));
        _gridLensDashed.Content = Localization.Text("DashedLines");
        _gridLensDashed.Style = (Style)_editor.FindResource("Ts.SwitchBox");
        _gridLensDashed.ToolTip = Localization.Text("DashedLinesHint");
        content.Children.Add(_gridLensDashed);
        content.Children.Add(new TextBlock
        {
            Text = Localization.Text("DragTheGridDirectlyOnThe"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        });
        _gridLensGroup.Content = content;
        _editor.settingsPanel.Children.Add(_gridLensGroup);
        UpdateGridLensColorButton();

        _gridLensVisible.Checked += (_, _) => UpdateGridLens();
        _gridLensVisible.Unchecked += (_, _) => UpdateGridLens();
        _gridLensDashed.Checked += (_, _) => UpdateGridLens();
        _gridLensDashed.Unchecked += (_, _) => UpdateGridLens();
        _gridLensOpacity.ValueChanged += (_, _) => UpdateGridLens();
        _gridLensColorButton.Click += (_, _) =>
        {
            var picker = new ColorReplaceWindow(_gridLensColor, Localization.Text("GridColor")) { Owner = Host };
            picker.ShowDialog();
            if (picker.SelectedColor is not { } color) return;
            _gridLensColor = color;
            UpdateGridLensColorButton();
            UpdateGridLens();
        };
        foreach (var preview in new[] { _editor.sourcePreview, _editor.resultPreview, _comparisonPreview })
            preview.GridMoved += (_, e) =>
            {
                _gridLensInputs.OffsetX.Value = e.OffsetX;
                _gridLensInputs.OffsetY.Value = e.OffsetY;
            };
    }

    internal static DockPanel GridLensRow(string label, FrameworkElement control)
    {
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        row.Children.Add(new TextBlock { Text = Localization.Text(label), Width = 122,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        AutomationProperties.SetName(control, Localization.Text(label));
        row.Children.Add(control);
        return row;
    }

    private void UpdateGridLensColorButton()
    {
        _gridLensColorButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new Border { Width = 20, Height = 16, Margin = new Thickness(0, 0, 8, 0),
                    BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray,
                    Background = new SolidColorBrush(Color.FromRgb(_gridLensColor.R, _gridLensColor.G, _gridLensColor.B)) },
                new TextBlock { Text = PaletteColorEntry.FormatHex(_gridLensColor), VerticalAlignment = VerticalAlignment.Center }
            }
        };
    }

    internal void UpdateGridLens()
    {
        var cells = _gridLensInputs.Grid;
        byte alpha = (byte)Math.Round(_gridLensOpacity.Value * 255 / 100);
        PreviewGridLens? grid = alpha > 0 && IsLensVisible("gridLens", IsLensOpen("gridLens") && _gridLensVisible.IsChecked == true)
            ? new PreviewGridLens(cells.Width, cells.Height, cells.OffsetX, cells.OffsetY,
                Color.FromArgb(alpha, _gridLensColor.R, _gridLensColor.G, _gridLensColor.B))
                { Dashed = _gridLensDashed.IsChecked == true }
            : null;
        foreach (var preview in new[] { _editor.sourcePreview, _editor.resultPreview })
            if (preview.GridLens != grid) preview.GridLens = grid;
        if (_comparisonPreview.GridLens is not null) _comparisonPreview.GridLens = null;
    }

    private sealed class LensGridInputs
    {
        public IntegerInput Width { get; } = new() { Minimum = 1, Maximum = 2048, Value = 16 };
        public IntegerInput Height { get; } = new() { Minimum = 1, Maximum = 2048, Value = 16 };
        public IntegerInput OffsetX { get; } = new() { Minimum = 0, Maximum = 15, Value = 0 };
        public IntegerInput OffsetY { get; } = new() { Minimum = 0, Maximum = 15, Value = 0 };
        public PreviewTileGrid Grid => new(Width.Value, Height.Value, OffsetX.Value, OffsetY.Value);

        public void AddRows(StackPanel content, Action update)
        {
            content.Children.Add(GridLensRow("HorizontalSpacingPx", Width));
            content.Children.Add(GridLensRow("VerticalSpacingPx", Height));
            content.Children.Add(GridLensRow("HorizontalOffsetPx", OffsetX));
            content.Children.Add(GridLensRow("VerticalOffsetPx", OffsetY));
            Width.ValueChanged += (_, _) =>
            {
                OffsetX.Maximum = Width.Value - 1;
                update();
            };
            Height.ValueChanged += (_, _) =>
            {
                OffsetY.Maximum = Height.Value - 1;
                update();
            };
            OffsetX.ValueChanged += (_, _) => update();
            OffsetY.ValueChanged += (_, _) => update();
        }
    }
}
