using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DrawingColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;

namespace DomainColorTest;

public sealed class PaletteColorEntry
{
    public DrawingColor Color { get; }
    public string Hex { get; }
    public SolidColorBrush Brush { get; }

    public PaletteColorEntry(DrawingColor color)
    {
        Color = DrawingColor.FromArgb(color.R, color.G, color.B);
        Hex = FormatHex(Color);
        Brush = new SolidColorBrush(MediaColor.FromRgb(color.R, color.G, color.B));
        Brush.Freeze();
    }

    public static string FormatHex(DrawingColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}

public partial class ColorReplaceWindow : Window
{
    private readonly IReadOnlyList<PaletteColorEntry> _colors;
    private bool _settingPalette;
    private bool _settingPickerText;
    private bool _draggingField;
    private bool _draggingHue;
    private double _hue;
    private double _saturation;
    private double _value;

    public event Action<DrawingColor, DrawingColor>? ReplaceRequested;

    public ColorReplaceWindow(DrawingColor oldColor, IReadOnlyList<PaletteColorEntry> colors, bool allowOldColorEdit)
    {
        InitializeComponent();
        _colors = colors;
        oldHexInput.IsReadOnly = !allowOldColorEdit;
        oldHexInput.Text = PaletteColorEntry.FormatHex(oldColor);
        newHexInput.Text = oldHexInput.Text;
        paletteList.ItemsSource = colors;
        paletteSection.Visibility = colors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        paletteList.Height = Math.Min(205, Math.Max(64, (int)Math.Ceiling(colors.Count / 10.0) * 35 + 26));
        Height = Math.Min(SystemParameters.WorkArea.Height - 50,
            colors.Count == 0 ? 650 : Math.Min(820, 680 + paletteList.Height));
        paletteList.SelectedItem = colors.FirstOrDefault(color => color.Color.ToArgb() == oldColor.ToArgb());

        oldHexInput.TextChanged += (_, _) => UpdateColorState(false);
        newHexInput.TextChanged += (_, _) =>
        {
            if (!_settingPickerText) UpdateColorState(true);
        };
        paletteList.SelectionChanged += PaletteSelectionChanged;
        Loaded += (_, _) =>
        {
            if (paletteList.SelectedItem is not null) paletteList.ScrollIntoView(paletteList.SelectedItem);
            RenderPicker();
        };
        UpdateColorState(true);
    }

    private void PaletteSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingPalette || paletteList.SelectedItem is not PaletteColorEntry selected) return;
        newHexInput.Text = selected.Hex;
    }

    private static bool TryReadColor(string text, out DrawingColor color)
    {
        color = default;
        if (text.Length != 7 || text[0] != '#' ||
            !int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            return false;
        color = DrawingColor.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        return true;
    }

    private void UpdateColorState(bool syncPicker)
    {
        bool oldValid = TryReadColor(oldHexInput.Text, out DrawingColor oldColor);
        bool newValid = TryReadColor(newHexInput.Text, out DrawingColor newColor);
        oldPreview.Background = oldValid
            ? new SolidColorBrush(MediaColor.FromRgb(oldColor.R, oldColor.G, oldColor.B))
            : System.Windows.Media.Brushes.Transparent;
        newPreview.Background = newValid
            ? new SolidColorBrush(MediaColor.FromRgb(newColor.R, newColor.G, newColor.B))
            : System.Windows.Media.Brushes.Transparent;
        if (newValid && syncPicker) SetPickerFromColor(newColor);
        RenderPicker();
        bool different = oldValid && newValid && oldColor.ToArgb() != newColor.ToArgb();
        replaceButton.IsEnabled = different;
        validationText.Text = !oldValid || !newValid ? "Введите цвет в формате #RRGGBB."
            : !different ? "Выберите новый цвет или введите HEX." : "";

        if (_settingPalette) return;
        _settingPalette = true;
        try
        {
            paletteList.SelectedItem = newValid
                ? _colors.FirstOrDefault(color => color.Color.ToArgb() == newColor.ToArgb()) : null;
        }
        finally { _settingPalette = false; }
    }

    private void SetPickerFromColor(DrawingColor color)
    {
        double red = color.R / 255.0, green = color.G / 255.0, blue = color.B / 255.0;
        double max = Math.Max(red, Math.Max(green, blue));
        double min = Math.Min(red, Math.Min(green, blue));
        double difference = max - min;
        if (difference == 0) _hue = 0;
        else if (max == red) _hue = 60 * (((green - blue) / difference) % 6);
        else if (max == green) _hue = 60 * ((blue - red) / difference + 2);
        else _hue = 60 * ((red - green) / difference + 4);
        if (_hue < 0) _hue += 360;
        _saturation = max == 0 ? 0 : difference / max;
        _value = max;
    }

    private static DrawingColor ColorFromPicker(double hue, double saturation, double value)
    {
        double chroma = value * saturation;
        double huePart = hue / 60;
        double secondary = chroma * (1 - Math.Abs(huePart % 2 - 1));
        double red = 0, green = 0, blue = 0;
        if (huePart < 1) { red = chroma; green = secondary; }
        else if (huePart < 2) { red = secondary; green = chroma; }
        else if (huePart < 3) { green = chroma; blue = secondary; }
        else if (huePart < 4) { green = secondary; blue = chroma; }
        else if (huePart < 5) { red = secondary; blue = chroma; }
        else { red = chroma; blue = secondary; }
        double shift = value - chroma;
        return DrawingColor.FromArgb(ToByte(red + shift), ToByte(green + shift), ToByte(blue + shift));
    }

    private static int ToByte(double value) => Math.Clamp((int)Math.Round(value * 255), 0, 255);

    private void RenderPicker()
    {
        DrawingColor hueColor = ColorFromPicker(_hue, 1, 1);
        hueSurface.Background = new SolidColorBrush(MediaColor.FromRgb(hueColor.R, hueColor.G, hueColor.B));
        double fieldWidth = colorField.ActualWidth > 0 ? colorField.ActualWidth : colorField.Width;
        double fieldHeight = colorField.ActualHeight > 0 ? colorField.ActualHeight : colorField.Height;
        double stripWidth = hueStrip.ActualWidth > 0 ? hueStrip.ActualWidth : hueStrip.Width;
        Canvas.SetLeft(fieldMarker, Math.Clamp(_saturation * fieldWidth - fieldMarker.Width / 2,
            0, fieldWidth - fieldMarker.Width));
        Canvas.SetTop(fieldMarker, Math.Clamp((1 - _value) * fieldHeight - fieldMarker.Height / 2,
            0, fieldHeight - fieldMarker.Height));
        Canvas.SetLeft(hueMarker, _hue / 360 * stripWidth - hueMarker.Width / 2);
        Canvas.SetTop(hueMarker, -3);
    }

    private void UpdateFromPicker()
    {
        DrawingColor color = ColorFromPicker(_hue, _saturation, _value);
        _settingPickerText = true;
        try { newHexInput.Text = PaletteColorEntry.FormatHex(color); }
        finally { _settingPickerText = false; }
        UpdateColorState(false);
    }

    private void ColorFieldMouseDown(object sender, MouseButtonEventArgs e)
    {
        _draggingField = true;
        colorField.CaptureMouse();
        UpdateFromField(e);
        e.Handled = true;
    }

    private void ColorFieldMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingField && e.LeftButton == MouseButtonState.Pressed) UpdateFromField(e);
    }

    private void ColorFieldMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingField) return;
        UpdateFromField(e);
        _draggingField = false;
        colorField.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void UpdateFromField(MouseEventArgs e)
    {
        Point position = e.GetPosition(colorField);
        _saturation = Math.Clamp(position.X / colorField.ActualWidth, 0, 1);
        _value = 1 - Math.Clamp(position.Y / colorField.ActualHeight, 0, 1);
        UpdateFromPicker();
    }

    private void HueStripMouseDown(object sender, MouseButtonEventArgs e)
    {
        _draggingHue = true;
        hueStrip.CaptureMouse();
        UpdateFromHue(e);
        e.Handled = true;
    }

    private void HueStripMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingHue && e.LeftButton == MouseButtonState.Pressed) UpdateFromHue(e);
    }

    private void HueStripMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingHue) return;
        UpdateFromHue(e);
        _draggingHue = false;
        hueStrip.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void UpdateFromHue(MouseEventArgs e)
    {
        _hue = Math.Clamp(e.GetPosition(hueStrip).X / hueStrip.ActualWidth, 0, 1) * 360;
        UpdateFromPicker();
    }

    private void PickerLostCapture(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(sender, colorField)) _draggingField = false;
        if (ReferenceEquals(sender, hueStrip)) _draggingHue = false;
    }

    private void ApplyColor(object sender, RoutedEventArgs e)
    {
        if (!TryReadColor(oldHexInput.Text, out DrawingColor oldColor) ||
            !TryReadColor(newHexInput.Text, out DrawingColor newColor) ||
            oldColor.ToArgb() == newColor.ToArgb()) return;
        Close();
        ReplaceRequested?.Invoke(oldColor, newColor);
    }

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
}
