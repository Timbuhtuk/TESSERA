using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using DrawingColor = System.Drawing.Color;

namespace DomainColorTest;

public partial class MainWindow
{
    private readonly GroupBox _gridLensGroup = new();
    private readonly CheckBox _gridLensVisible = new() { IsChecked = true };
    private readonly IntegerInput _gridLensWidth = new() { Minimum = 2, Maximum = 2048, Value = 16 };
    private readonly IntegerInput _gridLensHeight = new() { Minimum = 2, Maximum = 2048, Value = 16 };
    private readonly IntegerInput _gridLensOffsetX = new() { Minimum = 0, Maximum = 15, Value = 0 };
    private readonly IntegerInput _gridLensOffsetY = new() { Minimum = 0, Maximum = 15, Value = 0 };
    private readonly Button _gridLensColorButton = new();
    private DrawingColor _gridLensColor = DrawingColor.FromArgb(0, 200, 255);

    private void InitializeGridLens()
    {
        _gridLensGroup.Header = Localization.Text("Линза · сетка");
        var content = new StackPanel();
        _gridLensVisible.Content = Localization.Text("Показывать сетку");
        _gridLensVisible.Margin = new Thickness(0, 0, 0, 8);
        content.Children.Add(_gridLensVisible);
        content.Children.Add(GridLensRow("Шаг по X, px", _gridLensWidth));
        content.Children.Add(GridLensRow("Шаг по Y, px", _gridLensHeight));
        content.Children.Add(GridLensRow("Сдвиг по X, px", _gridLensOffsetX));
        content.Children.Add(GridLensRow("Сдвиг по Y, px", _gridLensOffsetY));
        _gridLensColorButton.HorizontalContentAlignment = HorizontalAlignment.Left;
        _gridLensColorButton.MinHeight = 30;
        content.Children.Add(GridLensRow("Цвет сетки", _gridLensColorButton));
        content.Children.Add(new TextBlock
        {
            Text = Localization.Text("Перетащите сетку мышью прямо на изображении. Она не попадёт в результат и экспорт."),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        });
        _gridLensGroup.Content = content;
        settingsPanel.Children.Add(_gridLensGroup);
        UpdateGridLensColorButton();

        _gridLensVisible.Checked += (_, _) => UpdateGridLens();
        _gridLensVisible.Unchecked += (_, _) => UpdateGridLens();
        _gridLensWidth.ValueChanged += (_, _) =>
        {
            _gridLensOffsetX.Maximum = _gridLensWidth.Value - 1;
            UpdateGridLens();
        };
        _gridLensHeight.ValueChanged += (_, _) =>
        {
            _gridLensOffsetY.Maximum = _gridLensHeight.Value - 1;
            UpdateGridLens();
        };
        _gridLensOffsetX.ValueChanged += (_, _) => UpdateGridLens();
        _gridLensOffsetY.ValueChanged += (_, _) => UpdateGridLens();
        _gridLensColorButton.Click += (_, _) =>
        {
            var picker = new ColorReplaceWindow(_gridLensColor, "Цвет сетки") { Owner = this };
            picker.ShowDialog();
            if (picker.SelectedColor is not { } color) return;
            _gridLensColor = color;
            UpdateGridLensColorButton();
            UpdateGridLens();
        };
        foreach (var preview in new[] { sourcePreview, resultPreview, _comparisonPreview })
            preview.GridMoved += (_, e) =>
            {
                _gridLensOffsetX.Value = e.OffsetX;
                _gridLensOffsetY.Value = e.OffsetY;
            };
    }

    private DockPanel GridLensRow(string label, FrameworkElement control)
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

    private void UpdateGridLens()
    {
        bool opened = _dockedTools.ContainsKey("gridLens") || _toolWindows.ContainsKey("gridLens");
        PreviewGridLens? grid = opened && _gridLensVisible.IsChecked == true
            ? new PreviewGridLens(_gridLensWidth.Value, _gridLensHeight.Value, _gridLensOffsetX.Value,
                _gridLensOffsetY.Value, Color.FromArgb(190, _gridLensColor.R, _gridLensColor.G, _gridLensColor.B))
            : null;
        foreach (var preview in new[] { sourcePreview, resultPreview, _comparisonPreview })
            if (preview.GridLens != grid) preview.GridLens = grid;
    }
}
