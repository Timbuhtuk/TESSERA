using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Tessera.Core.Imaging;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

internal sealed partial class EditorTools
{
    private sealed record SavedPalette(string Name, int[] Colors);

    private readonly List<SavedPalette> _customPalettes = [];

    private string _paletteStorePath = "";

    internal void InitializeCustomPalettes(string libraryDirectory)
    {
        _paletteStorePath = Path.Combine(libraryDirectory, "palettes.json");
        try
        {
            if (File.Exists(_paletteStorePath))
                _customPalettes.AddRange(JsonSerializer.Deserialize<List<SavedPalette>>(File.ReadAllText(_paletteStorePath))?
                    .Where(palette => !string.IsNullOrWhiteSpace(palette.Name) && palette.Colors is { Length: > 0 } &&
                        palette.Colors.All(color => color is >= 0 and <= 0xFFFFFF)) ?? []);
        }
        catch (Exception e) { Host.statusLabel.Text = Localization.Format("LoadPalettesFailed", e.Message); }
        foreach (var palette in _customPalettes) _editor.paletteInput.Items.Add(palette.Name);
        _editor.addPaletteButton.Click += AddCustomPalette;
        _editor.paletteInput.SelectionChanged += (_, _) =>
        {
            UpdatePaletteSwatches();
            System.Windows.Controls.ToolTipService.SetIsEnabled(_editor.paletteInput, _editor.paletteInput.SelectedIndex < 5);
        };
        UpdatePaletteSwatches();
    }

    private void AddCustomPalette(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Localization.Text("AddPaletteFromImage"),
            Filter = Localization.Text("PaletteImageFileFilter")
        };
        if (dialog.ShowDialog(Host) != true) return;
        try
        {
            using var image = new Bitmap(dialog.FileName);
            int[] colors = ImageColorTools.GetAllVisibleColors(image).Select(color => color.ToArgb() & 0xFFFFFF).ToArray();
            if (colors.Length == 0) throw new InvalidOperationException(Localization.Text("ImageHasNoVisibleColors"));
            var palette = new SavedPalette(Path.GetFileNameWithoutExtension(dialog.FileName), colors);
            var saved = _customPalettes.Append(palette).ToArray();
            string temp = _paletteStorePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(saved));
            File.Move(temp, _paletteStorePath, true);
            _customPalettes.Add(palette);
            _editor.paletteInput.Items.Add(palette.Name);
            _editor.paletteColorModeInput.IsChecked = true;
            _editor.paletteInput.SelectedIndex = 5 + _customPalettes.Count - 1;
            Host.statusLabel.Text = Localization.Format("PaletteColorCountStatus", palette.Name, colors.Length);
        }
        catch (Exception error)
        {
            MessageBox.Show(Host, error.Message, Localization.Text("AddPaletteFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdatePaletteSwatches()
    {
        int index = _editor.paletteInput.SelectedIndex;
        IReadOnlyList<int> colors = index >= 5 && index - 5 < _customPalettes.Count
            ? _customPalettes[index - 5].Colors
            : index is >= 0 and <= 3 ? Palettes.GetPalette((EPaletteKind)(index + 1)) : [];
        _editor.paletteSwatches.SetColors(colors);
        _editor.paletteSwatches.Visibility = colors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _editor.paletteColorCount.Text = Localization.Format("PaletteColorCount", colors.Distinct().Count());
        _editor.paletteColorCount.Visibility = colors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private int[]? SelectedCustomPalette()
    {
        int index = _editor.paletteInput.SelectedIndex - 5;
        return index >= 0 && index < _customPalettes.Count ? _customPalettes[index].Colors.ToArray() : null;
    }

    private void RestoreCustomPalette(DownscaleOptions options)
    {
        if (options.Palette != EPaletteKind.Custom || options.CustomPalette is not { Length: > 0 } colors) return;
        int index = _customPalettes.FindIndex(palette => palette.Colors.SequenceEqual(colors));
        if (index < 0)
        {
            _customPalettes.Add(new SavedPalette(Localization.Text("PaletteFromHistory"), colors.ToArray()));
            _editor.paletteInput.Items.Add(_customPalettes[^1].Name);
            index = _customPalettes.Count - 1;
        }
        _editor.paletteInput.SelectedIndex = index + 5;
    }
}
