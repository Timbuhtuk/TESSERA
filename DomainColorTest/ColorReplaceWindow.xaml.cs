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
    private IReadOnlyList<ColorArea> _areas = [];
    private bool _paletteTargetsSource;
    private bool _paletteExpanded;
    private bool _pickingSource;
    private bool _pickingTarget;
    private bool _settingPickerText;
    private bool _draggingField;
    private bool _draggingHue;
    private bool _choosingColor;
    private double _hue;
    private double _saturation;
    private double _value;

    public event Action<DrawingColor, DrawingColor>? ReplaceRequested;
    public event Action? PickSourceRequested;
    public event Action? PickTargetRequested;
    public event Action? PaletteRequested;
    public event Action? PickCanceled;
    public Func<DrawingColor, long>? CountMatches { get; set; }
    public event Action<DrawingColor?>? SourceColorChanged;
    public DrawingColor? SelectedColor { get; private set; }

    public ColorReplaceWindow(DrawingColor initialColor) : this(initialColor, [], false)
    {
        _choosingColor = true;
        sourcePickerButton.Visibility = Visibility.Collapsed;
        Title = Localization.Text("Цвет фона · TESSERA");
        dialogTitle.Text = Localization.Text("Цвет фона");
        oldColorSection.Visibility = oldColorPanel.Visibility = Visibility.Collapsed;
        targetPickerButton.Visibility = Visibility.Collapsed;
        singleColorPicker.Visibility = Visibility.Visible;
        newColorLabel.Text = Localization.Text("Выберите цвет");
        newColorLabel.Margin = new Thickness(0, 0, 0, 10);
        replaceButton.Content = Localization.Text("Выбрать цвет");
        Height = Math.Min(SystemParameters.WorkArea.Height - 50, 620);
        UpdateColorState(true);
    }

    public ColorReplaceWindow(DrawingColor initialColor, string title) : this(initialColor)
    {
        Title = Localization.Text(title) + " · TESSERA";
        dialogTitle.Text = Localization.Text(title);
    }

    public ColorReplaceWindow(DrawingColor oldColor, IReadOnlyList<PaletteColorEntry> colors, bool allowOldColorEdit)
    {
        InitializeComponent();
        oldColorLabel.Text = Localization.Text("Цвет 1");
        newColorLabel.Text = Localization.Text("Цвет 2");
        sourcePickerButton.Content = targetPickerButton.Content = Localization.Text("Пипетка");
        System.Windows.Automation.AutomationProperties.SetName(oldSwatchButton, Localization.Text("Палитра цвета 1"));
        System.Windows.Automation.AutomationProperties.SetName(newSwatchButton, Localization.Text("Палитра цвета 2"));
        expandPaletteButton.ToolTip = Localization.Text("Развернуть карту цветов");
        targetPickerButton.ToolTip = Localization.Text("Выбрать цвет с исходника, результата или палитры внутри Tessera.");
        sourcePickerButton.ToolTip = Localization.Text("Нажмите, затем выберите пиксель на изображении основы в редакторе. Esc отменяет пипетку.");
        oldHexInput.IsReadOnly = false;
        oldHexInput.Text = PaletteColorEntry.FormatHex(oldColor);
        newHexInput.Text = oldHexInput.Text;
        SetPalette(colors.Select(c => new ColorArea(c.Color, 1)).ToArray());
        replacementPalette.ColorSelected += color =>
        {
            if (_paletteTargetsSource) SetSourceColor(color); else SetTargetColor(color);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || (!_pickingSource && !_pickingTarget)) return;
            SetPicking(false, false); PickCanceled?.Invoke(); e.Handled = true;
        };
        oldHexInput.TextChanged += (_, _) => UpdateColorState(false);
        newHexInput.TextChanged += (_, _) =>
        {
            if (!_settingPickerText) UpdateColorState(true);
        };
        Loaded += (_, _) =>
        {
            RenderPicker();
        };
        UpdateColorState(true);
    }

    public void SetPalette(IReadOnlyList<ColorArea> areas, int? totalColors = null)
    {
        _areas = areas;
        replacementPalette.SetAreas(areas);
        paletteSection.Visibility = areas.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        paletteTitle.Text = Localization.Format("Палитра · {0:N0} цветов", totalColors ?? areas.Count(a => a.Color is not null));
    }

    public void SetBasis(bool result) => basisBadge.Text = Localization.Text(result ? "Результат" : "Исходник");
    public void SetPicking(bool source, bool target)
    {
        _pickingSource = source; _pickingTarget = target;
        if (source || target) _paletteTargetsSource = source;
        sourcePickerButton.Content = Localization.Text(source ? "Отмена (Esc)" : "Пипетка");
        targetPickerButton.Content = Localization.Text(target ? "Отмена (Esc)" : "Пипетка");
    }
    public void SetTargetColor(DrawingColor color) => newHexInput.Text = PaletteColorEntry.FormatHex(color);
    private void OpenOldPalette(object sender, RoutedEventArgs e) => OpenPalette(true);
    private void OpenNewPalette(object sender, RoutedEventArgs e) => OpenPalette(false);
    private void OpenPalette(bool source)
    {
        _paletteTargetsSource = source;
        oldColorSection.BorderBrush = TryFindResource(source ? "Ts.Focus" : "Ts.BorderStrong") as Brush;
        paletteSection.Visibility = _areas.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PaletteRequested?.Invoke();
        replacementPalette.Focus();
    }
    private void ExpandPalette(object sender, RoutedEventArgs e)
    {
        _paletteExpanded = !_paletteExpanded;
        replacementPalette.Height = _paletteExpanded ? 320 : 128;
        Height = Math.Min(SystemParameters.WorkArea.Height - 50, _paletteExpanded ? 727 : 535);
        expandPaletteButton.Content = _paletteExpanded ? "↙" : "↗";
        expandPaletteButton.ToolTip = Localization.Text(_paletteExpanded ? "Свернуть карту цветов" : "Развернуть карту цветов");
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
        if (!_choosingColor) SourceColorChanged?.Invoke(oldValid ? oldColor : null);
        oldPreview.Background = oldValid
            ? new SolidColorBrush(MediaColor.FromRgb(oldColor.R, oldColor.G, oldColor.B))
            : System.Windows.Media.Brushes.Transparent;
        newPreview.Background = newValid
            ? new SolidColorBrush(MediaColor.FromRgb(newColor.R, newColor.G, newColor.B))
            : System.Windows.Media.Brushes.Transparent;
        if (newValid && syncPicker) SetPickerFromColor(newColor);
        if (_choosingColor) RenderPicker();
        long matches = oldValid ? CountMatches?.Invoke(oldColor) ?? 1 : 0;
        bool different = _choosingColor ? newValid : oldValid && newValid && oldColor.ToArgb() != newColor.ToArgb();
        replaceButton.IsEnabled = different && (_choosingColor || matches > 0);
        validationText.Text = (!oldValid && !_choosingColor) || !newValid ? Localization.Text("Введите цвет в формате #RRGGBB.")
            : !_choosingColor && matches == 0 ? Localization.Text("Совпадений: 0") : "";
        validationText.Visibility = validationText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    }

    public void RefreshValidation() => UpdateColorState(false);

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
        if (_choosingColor)
        {
            if (!TryReadColor(newHexInput.Text, out DrawingColor selected)) return;
            SelectedColor = selected;
            Close();
            return;
        }
        if (!replaceButton.IsEnabled || !TryReadColor(oldHexInput.Text, out DrawingColor oldColor) ||
            !TryReadColor(newHexInput.Text, out DrawingColor newColor) ||
            oldColor.ToArgb() == newColor.ToArgb()) return;
        Close();
        ReplaceRequested?.Invoke(oldColor, newColor);
    }

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();

    public void SetSourceColor(DrawingColor color) => oldHexInput.Text = PaletteColorEntry.FormatHex(color);

    private void PickSourceColor(object sender, RoutedEventArgs e) => PickSourceRequested?.Invoke();
    private void PickTargetColor(object sender, RoutedEventArgs e) => PickTargetRequested?.Invoke();
}
