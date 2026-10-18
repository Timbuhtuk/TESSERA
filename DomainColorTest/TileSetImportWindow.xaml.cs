using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PixelArtDownscale;

namespace DomainColorTest;

public partial class TileSetImportWindow : Window
{
    private bool _ready, _updating, _loading, _closed;
    private int _version;
    private BitmapSource? _source;
    private string? _path;
    public TileSetLayout? Layout { get; private set; }
    public IReadOnlyList<TileAsset> ImportedAssets { get; private set; } = [];

    public TileSetImportWindow()
    {
        InitializeComponent(); _ready = true;
        Closed += (_, _) => { _closed = true; _version++; };
    }

    public async Task LoadFileAsync(string path)
    {
        int version = ++_version;
        _loading = true; UpdateButtons();
        statusLabel.Text = "Загрузка тайлсета…";
        try
        {
            string fullPath = Path.GetFullPath(path);
            var source = await Task.Run(() => TileAsset.DecodeFile(fullPath));
            if (_closed || version != _version) return;
            _source = source; _path = fullPath;
            atlasPreview.SetImage(source);
            sourceLabel.Text = $"{Path.GetFileName(fullPath)} · {source.PixelWidth} × {source.PixelHeight} px";
            sourceLabel.ToolTip = fullPath;
            await DetectAsync();
        }
        catch (Exception ex) { if (!_closed && version == _version) statusLabel.Text = "Не удалось открыть тайлсет: " + ex.Message; }
        finally { if (!_closed && version == _version) { _loading = false; UpdateLayoutGrid(); UpdateButtons(); } }
    }

    public async Task DetectAsync()
    {
        if (_source is null) return;
        int version = _version;
        var source = _source;
        try
        {
            var (mx, my, sx, sy) = ReadInsets();
            _loading = true; UpdateButtons();
            statusLabel.Text = "Подбор размера тайла…";
            var suggestions = await Task.Run(() =>
            {
                byte[] pixels = new byte[checked(source.PixelWidth * source.PixelHeight * 4)];
                source.CopyPixels(pixels, source.PixelWidth * 4, 0);
                return TileSetSlicer.Suggest(pixels, source.PixelWidth, source.PixelHeight, mx, my, sx, sy);
            });
            if (_closed || version != _version) return;
            _updating = true;
            suggestionsInput.ItemsSource = suggestions;
            suggestionsInput.SelectedIndex = suggestions.Count > 0 ? 0 : -1;
            _updating = false;
            if (suggestions.Count > 0) ApplySuggestion(suggestions[0]);
            else
            {
                SetGridInputs(0, source.PixelWidth, source.PixelHeight);
                suggestionHint.Text = "Подходящая типовая сетка не найдена. Укажите размер тайла или число столбцов и строк вручную.";
            }
            statusLabel.Text = "Проверьте предложенные границы и подтвердите импорт.";
        }
        catch (Exception ex) { if (!_closed && version == _version) statusLabel.Text = "Автоподбор недоступен: " + ex.Message; }
        finally { if (!_closed && version == _version) { _loading = false; UpdateLayoutGrid(); UpdateButtons(); } }
    }

    private (int Mx, int My, int Sx, int Sy) ReadInsets() =>
        (ReadInt(marginXInput, false), ReadInt(marginYInput, false), ReadInt(spacingXInput, false), ReadInt(spacingYInput, false));

    private static int ReadInt(TextBox box, bool positive)
    {
        if (!int.TryParse(box.Text, out int value) || value < (positive ? 1 : 0)) throw new ArgumentException("Введите целые размеры: тайл и сетка — от 1, отступы и промежутки — от 0.");
        return value;
    }

    private void UpdateLayoutGrid()
    {
        if (!_ready || _updating || _source is null) return;
        try
        {
            var (mx, my, sx, sy) = ReadInsets();
            int first = ReadInt(firstInput, true), second = ReadInt(secondInput, true);
            Layout = modeInput.SelectedIndex == 1
                ? TileSetLayout.FromGrid(_source.PixelWidth, _source.PixelHeight, first, second, mx, my, sx, sy)
                : TileSetLayout.FromTileSize(_source.PixelWidth, _source.PixelHeight, first, second, mx, my, sx, sy);
            layoutSummary.Text = $"{_source.PixelWidth} × {_source.PixelHeight} px → {Layout.Columns} столбцов × {Layout.Rows} строк → {Layout.Count} тайлов по {Layout.TileWidth} × {Layout.TileHeight} px";
        }
        catch (Exception ex) { Layout = null; layoutSummary.Text = ex.Message; }
        atlasPreview.SetLayout(Layout); UpdateButtons();
    }

    private void UpdateButtons()
    {
        if (!_ready) return;
        settingsPanel.IsEnabled = !_loading;
        detectButton.IsEnabled = !_loading && _source is not null;
        openAtlasButton.IsEnabled = !_loading;
        importButton.IsEnabled = !_loading && Layout is not null;
    }

    private void SetGridInputs(int mode, int first, int second)
    {
        _updating = true;
        modeInput.SelectedIndex = mode;
        firstInput.Text = first.ToString(); secondInput.Text = second.ToString();
        firstLabel.Text = mode == 1 ? "Столбцов" : "Ширина тайла, px";
        secondLabel.Text = mode == 1 ? "Строк" : "Высота тайла, px";
        _updating = false; UpdateLayoutGrid();
    }

    private void ApplySuggestion(TileSetSuggestion suggestion)
    {
        SetGridInputs(0, suggestion.Layout.TileWidth, suggestion.Layout.TileHeight);
        suggestionHint.Text = suggestion.Reason;
    }

    public IReadOnlyList<TileAsset> BuildAssets()
    {
        if (_loading || _source is null || _path is null || Layout is null) throw new InvalidOperationException("Выберите изображение и корректную сетку.");
        var assets = new List<TileAsset>();
        foreach (var region in TileSetSlicer.GetRegions(_source.PixelWidth, _source.PixelHeight, Layout))
        {
            var cropped = new CroppedBitmap(_source, new Int32Rect(region.X, region.Y, region.Width, region.Height));
            var asset = new TileAsset(cropped)
            {
                Name = $"{Path.GetFileNameWithoutExtension(_path)} [r{region.Row + 1:D2},c{region.Column + 1:D2}]",
                AtlasSourcePath = _path
            };
            if (skipEmptyInput.IsChecked == true && !Enumerable.Range(0, asset.Width * asset.Height).Any(q => asset.Pixels[q * 4 + 3] != 0)) continue;
            assets.Add(asset);
        }
        return assets;
    }

    private async void OpenAtlasClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Открыть тайлсет", Filter = "Изображения|*.png;*.bmp;*.gif;*.jpg;*.jpeg;*.tif;*.tiff|Все файлы|*.*" };
        if (dialog.ShowDialog(this) == true) await LoadFileAsync(dialog.FileName);
    }
    private async void DetectClicked(object sender, RoutedEventArgs e) => await DetectAsync();
    private void UseThreeBySix(object sender, RoutedEventArgs e) => SetGridInputs(1, 3, 6);
    private void ModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updating) return;
        int mode = modeInput.SelectedIndex;
        if (Layout is { } layout) SetGridInputs(mode, mode == 1 ? layout.Columns : layout.TileWidth, mode == 1 ? layout.Rows : layout.TileHeight);
        else SetGridInputs(mode, mode == 1 ? 3 : 32, mode == 1 ? 6 : 32);
    }
    private void SuggestionChanged(object sender, SelectionChangedEventArgs e)
    { if (_ready && !_updating && suggestionsInput.SelectedItem is TileSetSuggestion suggestion) ApplySuggestion(suggestion); }
    private void GridTextChanged(object sender, TextChangedEventArgs e) => UpdateLayoutGrid();
    private void ImportClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            ImportedAssets = BuildAssets();
            if (ImportedAssets.Count == 0) { statusLabel.Text = "Все тайлы прозрачные. Отключите пропуск пустых ячеек или выберите другой тайлсет."; return; }
            DialogResult = true;
        }
        catch (Exception ex) { statusLabel.Text = "Не удалось разрезать тайлсет: " + ex.Message; }
    }
    private void AtlasDragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_loading && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths && File.Exists(paths[0]) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void AtlasDropped(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!_loading && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths) await LoadFileAsync(paths[0]);
    }
}
