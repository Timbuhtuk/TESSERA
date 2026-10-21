using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DomainColorTest;

public partial class TileInspectorWindow
{
    private TileAsset? _strokeAsset, _historyAsset;
    private TileEditSnapshot? _strokeBefore;
    private TilePointer? _strokeStart, _lastPoint;
    private Color _strokeColor;
    private int _strokeSize;

    private void InitializePainting()
    {
        preview.PaintRequested += HandlePaint;
        preview.StrokeEnded += FinishStroke;
        paintColorInput.TextChanged += (_, _) => { if (_ready) SetProjectDirty(); };
        smoothPassesInput.TextChanged += (_, _) => { if (_ready) SetProjectDirty(); };
    }

    private static bool TryColor(string text, out Color color)
    {
        color = Colors.White;
        string value = text.Trim().TrimStart('#');
        if (value.Length is not (6 or 8) || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint packed)) return false;
        color = Color.FromArgb(value.Length == 6 ? (byte)255 : (byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
        return true;
    }

    private void HandlePaint(TilePointer point, bool begin)
    {
        if (_busy || paintToolInput.SelectedIndex == 0) return;
        var target = point.Companion ? CompanionAsset : ActiveAsset;
        if (target is null) return;
        if (paintToolInput.SelectedIndex == 3)
        {
            if (begin)
            {
                paintColorInput.Text = target.GetPixel(point.X, point.Y).ToString();
                statusLabel.Text = Localization.Text("Цвет взят из исходных пикселей · ") + paintColorInput.Text;
            }
            return;
        }
        if (begin)
        {
            FinishStroke();
            if (paintToolInput.SelectedIndex == 2) _strokeColor = Colors.Transparent;
            else if (!TryColor(paintColorInput.Text, out _strokeColor)) { statusLabel.Text = Localization.Text("Введите цвет #RRGGBB или #AARRGGBB."); return; }
            _strokeAsset = target; _strokeBefore = target.Capture();
            _strokeStart = _lastPoint = point;
            _strokeSize = brushSizeInput.SelectedIndex == 1 ? 2 : brushSizeInput.SelectedIndex == 2 ? 4 : 1;
        }
        // A stroke stays on the copy where it began; crossing a seam cannot paint a different asset.
        if (_strokeAsset != target || _strokeStart is null || point.Column != _strokeStart.Column || point.Row != _strokeStart.Row) return;
        target.PaintLine(_lastPoint!.X, _lastPoint.Y, point.X, point.Y, _strokeColor, _strokeSize);
        _lastPoint = point; _historyAsset = target;
        UpdateAnalysis();
    }

    private void FinishStroke()
    {
        if (_strokeAsset is null || _strokeBefore is null) return;
        if (_strokeAsset.FinishEdit(_strokeBefore)) SetProjectDirty();
        _strokeAsset = null; _strokeBefore = null; _strokeStart = _lastPoint = null;
        UpdateEditControls();
    }

    private void UpdateEditControls()
    {
        if (!_ready) return;
        UpdateWorkspaceMaterialControls();
        var history = _historyAsset is not null && _assets.Contains(_historyAsset) ? _historyAsset : ActiveAsset;
        undoButton.IsEnabled = !_busy && history?.CanUndo == true;
        redoButton.IsEnabled = !_busy && history?.CanRedo == true;
        smoothButton.IsEnabled = !_busy && ActiveAsset is not null;
        exportButton.IsEnabled = !_busy && ActiveAsset is not null;
        reloadButton.IsEnabled = !_busy && ActiveAsset?.SourcePath is not null;
        assetList.IsEnabled = companionInput.IsEnabled = !_busy;
        paintToolInput.IsEnabled = !_busy;
        preview.EditingEnabled = !_busy && paintToolInput.SelectedIndex > 0;
        preview.Cursor = preview.EditingEnabled ? Cursors.Cross : Cursors.Arrow;
        undoButton.ToolTip = Localization.Format("Отменить: {0} (Ctrl+Z)", history?.Name ?? Localization.Text("тайл"));
        redoButton.ToolTip = Localization.Format("Повторить: {0} (Ctrl+Y)", history?.Name ?? Localization.Text("тайл"));
        editHint.Text = Localization.Format("Рисование — по копии A или B. Сглаживание — весь A / его края. Отмена: {0}.", history?.Name ?? Localization.Text("активный тайл"));
    }

    private void UndoClicked(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        FinishStroke();
        var asset = _historyAsset ?? ActiveAsset;
        if (asset?.CanUndo != true) return;
        asset.Undo(); SetProjectDirty(); RefreshActiveAsset();
    }
    private void RedoClicked(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var asset = _historyAsset ?? ActiveAsset;
        if (asset?.CanRedo != true) return;
        asset.Redo(); SetProjectDirty(); RefreshActiveAsset();
    }
    private void PaintToolChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        FinishStroke(); UpdateEditControls();
    }
    private void ChoosePaintColor(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        TryColor(paintColorInput.Text, out var color);
        var picker = new ColorReplaceWindow(System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B)) { Owner = this };
        picker.ShowDialog();
        if (picker.SelectedColor is { } selected) paintColorInput.Text = $"#{color.A:X2}{selected.R:X2}{selected.G:X2}{selected.B:X2}";
    }

    public async Task SmoothActiveAsync(int passes, bool edgesOnly)
    {
        if (_busy || ActiveAsset is not { } asset) return;
        FinishStroke();
        if (passes is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(passes), Localization.Text("Число проходов: 1–100."));
        byte[] pixels = (byte[])asset.Pixels.Clone();
        int band = (int)stripeInput.Value;
        long version = asset.Version;
        _busy = true; UpdateEditControls();
        statusLabel.Text = Localization.Format("Сглаживание цветов · {0} · {1} проходов…", asset.Name, passes);
        try
        {
            byte[] output = await Task.Run(() => TileAssetProcessing.Smooth(pixels, asset.Width, asset.Height, passes, edgesOnly, band));
            if (_closed || !_assets.Contains(asset) || asset.Version != version) return;
            _historyAsset = asset;
            if (asset.ReplacePixels(output)) { SetProjectDirty(); UpdateAnalysis(); statusLabel.Text = Localization.Text("Цвета сглажены · можно отменить правку."); }
            else statusLabel.Text = Localization.Text("Сглаживание завершено · подходящих изменений нет.");
        }
        finally { _busy = false; RefreshWorkspaceMaterials(); UpdateEditControls(); }
    }
    private async void SmoothClicked(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(smoothPassesInput.Text, out int passes) || passes is < 1 or > 100)
        { statusLabel.Text = Localization.Text("Число проходов должно быть от 1 до 100."); return; }
        try { await SmoothActiveAsync(passes, smoothEdgesInput.IsChecked == true); }
        catch (Exception ex) { statusLabel.Text = Localization.Text("Не удалось сгладить цвета: ") + ex.Message; }
    }
}
