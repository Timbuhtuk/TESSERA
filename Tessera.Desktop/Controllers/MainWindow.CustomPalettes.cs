using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Tessera.Core.Imaging;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

public partial class MainWindow
{
    private sealed record SavedPalette(string Name, int[] Colors);
    private readonly List<SavedPalette> _customPalettes = [];
    private string _paletteStorePath = "";

    private void InitializeCustomPalettes(string libraryDirectory)
    {
        _paletteStorePath = Path.Combine(libraryDirectory, "palettes.json");
        try
        {
            if (File.Exists(_paletteStorePath))
                _customPalettes.AddRange(JsonSerializer.Deserialize<List<SavedPalette>>(File.ReadAllText(_paletteStorePath))?
                    .Where(palette => !string.IsNullOrWhiteSpace(palette.Name) && palette.Colors is { Length: > 0 } &&
                        palette.Colors.All(color => color is >= 0 and <= 0xFFFFFF)) ?? []);
        }
        catch (Exception e) { statusLabel.Text = Localization.Format("Не удалось загрузить палитры: {0}", e.Message); }
        foreach (var palette in _customPalettes) paletteInput.Items.Add(palette.Name);
        addPaletteButton.Click += AddCustomPalette;
        paletteSwatchScroll.ScrollChanged += (_, _) => paletteSwatches.InvalidateVisual();
        paletteInput.SelectionChanged += (_, _) =>
        {
            UpdatePaletteSwatches();
            bool builtIn = paletteInput.SelectedIndex < 5;
            if (!builtIn && paletteInput.ToolTip is System.Windows.Controls.ToolTip tip)
                tip.IsOpen = false;
            System.Windows.Controls.ToolTipService.SetIsEnabled(paletteInput, builtIn);
        };
        UpdatePaletteSwatches();
    }

    private void AddCustomPalette(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Localization.Text("Добавить палитру из изображения"),
            Filter = Localization.Text("Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif")
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ImportCustomPalette(dialog.FileName);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, Localization.Text("Не удалось добавить палитру"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportCustomPalette(string path)
    {
        using var image = new Bitmap(path);
        int[] colors = ImageColorTools.GetAllVisibleColors(image).Select(ColorSpace.Pack).ToArray();
        if (colors.Length == 0)
            throw new InvalidOperationException(Localization.Text("На изображении нет видимых цветов."));

        var palette = new SavedPalette(Path.GetFileNameWithoutExtension(path), colors);
        string temp = _paletteStorePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(_customPalettes.Append(palette)));
            File.Move(temp, _paletteStorePath, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }

        _customPalettes.Add(palette);
        paletteInput.Items.Add(palette.Name);
        paletteColorModeInput.IsChecked = true;
        paletteInput.SelectedIndex = 5 + _customPalettes.Count - 1;
        statusLabel.Text = Localization.Format("Палитра «{0}»: {1} цветов.", palette.Name, colors.Length);
    }

    private void UpdatePaletteSwatches()
    {
        int index = paletteInput.SelectedIndex;
        IReadOnlyList<int> colors = index >= 5 && index - 5 < _customPalettes.Count
            ? _customPalettes[index - 5].Colors
            : index is >= 0 and <= 3 ? Palettes.GetPalette((EPaletteKind)(index + 1)).Distinct().ToArray() : [];
        paletteSwatches.SetColors(colors);
        paletteSwatchScroll.ScrollToTop();
        paletteSwatchFrame.Visibility = colors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        paletteColorCount.Text = Localization.Format("{0} цветов", colors.Count);
        paletteColorCount.Visibility = colors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private int[]? SelectedCustomPalette()
    {
        int index = paletteInput.SelectedIndex - 5;
        return index >= 0 && index < _customPalettes.Count ? _customPalettes[index].Colors.ToArray() : null;
    }

    private void RestoreCustomPalette(DownscaleOptions options)
    {
        if (options.Palette != EPaletteKind.Custom || options.CustomPalette is not { Length: > 0 } colors) return;
        int index = _customPalettes.FindIndex(palette => palette.Colors.SequenceEqual(colors));
        if (index < 0)
        {
            _customPalettes.Add(new SavedPalette(Localization.Text("Палитра из истории"), colors.ToArray()));
            paletteInput.Items.Add(_customPalettes[^1].Name);
            index = _customPalettes.Count - 1;
        }
        paletteInput.SelectedIndex = index + 5;
    }
}
