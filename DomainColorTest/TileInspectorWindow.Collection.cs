using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace DomainColorTest;

public partial class TileInspectorWindow
{
    private void SetProjectDirty()
    {
        if (_settingCollection) return;
        _projectDirty = true;
        UpdateProjectTitle();
    }

    private void UpdateProjectTitle() => Title = $"Проверка тайлинга · {(_projectPath is null ? "TESSERA" : Path.GetFileName(_projectPath))}{(_projectDirty ? " *" : "")}";

    public void SaveCollectionToFile(string path)
    {
        if (_busy) throw new InvalidOperationException("Дождитесь сглаживания.");
        FinishStroke();
        var settings = new TileCollectionSettings
        {
            Selected = ActiveAsset?.Id, Companion = CompanionAsset?.Id,
            RepeatX = repeatXInput.IsChecked == true, RepeatY = repeatYInput.IsChecked == true,
            Copies = copiesInput.SelectedIndex, Zoom = zoomInput.SelectedIndex, Background = backgroundInput.SelectedIndex,
            Heat = heatInput.IsChecked == true, Guides = guidesInput.IsChecked == true,
            Threshold = thresholdInput.Value, Opacity = opacityInput.Value, Stripe = stripeInput.Value,
            Color = paintColorInput.Text, BrushSize = brushSizeInput.SelectedIndex,
            Passes = int.TryParse(smoothPassesInput.Text, out int passes) ? Math.Clamp(passes, 1, 100) : 1,
            SmoothEdgesOnly = smoothEdgesInput.IsChecked == true
        };
        TileCollectionFile.Save(path, _assets, settings);
        _projectPath = Path.GetFullPath(path); _projectDirty = false;
        UpdateProjectTitle(); statusLabel.Text = "Коллекция и правки сохранены · " + Path.GetFileName(path);
    }

    public void LoadCollectionFromFile(string path)
    {
        if (_busy) throw new InvalidOperationException("Дождитесь сглаживания.");
        // Read the complete project before changing the current session.
        var loaded = TileCollectionFile.Load(path);
        FinishStroke(); _loadVersion++;
        _reloadTimer.Stop(); _changedPaths.Clear();
        _settingCollection = true;
        try
        {
            _assets.Clear();
            foreach (var asset in loaded.Assets) _assets.Add(asset);
            var s = loaded.Settings;
            assetList.SelectedItem = _assets.FirstOrDefault(asset => asset.Id == s.Selected) ?? _assets.FirstOrDefault();
            repeatXInput.IsChecked = s.RepeatX; repeatYInput.IsChecked = s.RepeatY;
            copiesInput.SelectedIndex = Math.Clamp(s.Copies, 0, 2);
            zoomInput.SelectedIndex = Math.Clamp(s.Zoom, 0, 6);
            backgroundInput.SelectedIndex = Math.Clamp(s.Background, 0, 2);
            heatInput.IsChecked = s.Heat; guidesInput.IsChecked = s.Guides;
            thresholdInput.Value = Finite(s.Threshold, 0, 30, 5);
            opacityInput.Value = Finite(s.Opacity, 0, 100, 70);
            stripeInput.Value = Finite(s.Stripe, 1, 8, 1);
            paintColorInput.Text = s.Color ?? "#FFFFFFFF";
            brushSizeInput.SelectedIndex = Math.Clamp(s.BrushSize, 0, 2);
            smoothPassesInput.Text = Math.Clamp(s.Passes, 1, 100).ToString();
            smoothEdgesInput.IsChecked = s.SmoothEdgesOnly;
            paintToolInput.SelectedIndex = 0;
        }
        finally { _settingCollection = false; }
        _historyAsset = ActiveAsset;
        RefreshActiveAsset();
        if (companionInput.Items.Cast<CompanionChoice>().Any(choice => choice.Asset?.Id == loaded.Settings.Companion))
            SelectCompanion(loaded.Settings.Companion);
        _projectPath = Path.GetFullPath(path); _projectDirty = false;
        UpdateProjectTitle(); ConfigureWatchers();
        if (ActiveAsset is not null) assetList.ScrollIntoView(ActiveAsset);
        statusLabel.Text = $"Коллекция открыта · {_assets.Count} ассетов";
    }

    private static double Finite(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private bool SaveCollectionWithDialog()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить коллекцию тайлов", Filter = "Коллекция Tessera|*.ttiles", DefaultExt = ".ttiles",
            AddExtension = true, FileName = _projectPath is null ? "tiles.ttiles" : Path.GetFileName(_projectPath),
            InitialDirectory = _projectPath is null ? "" : Path.GetDirectoryName(_projectPath)
        };
        if (dialog.ShowDialog(this) != true) return false;
        try { SaveCollectionToFile(dialog.FileName); return true; }
        catch (Exception ex) { statusLabel.Text = "Не удалось сохранить коллекцию: " + ex.Message; return false; }
    }

    private bool ConfirmReplaceCollection()
    {
        FinishStroke();
        if (_allowClose || !_projectDirty) return true;
        var answer = TesseraDialog.Show(this, "Сохранить изменения коллекции перед закрытием или открытием другого проекта?", "Коллекция тайлов", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer == MessageBoxResult.No || answer == MessageBoxResult.Yes && SaveCollectionWithDialog();
    }

    private void ConfirmWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_busy) { e.Cancel = true; statusLabel.Text = "Дождитесь завершения сглаживания."; return; }
        if (!ConfirmReplaceCollection()) e.Cancel = true;
    }

    public void DiscardAndClose() { _allowClose = true; Close(); }

    private void SaveCollectionClicked(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_projectPath is null || sender is System.Windows.Controls.Button) SaveCollectionWithDialog();
        else
        {
            try { SaveCollectionToFile(_projectPath); }
            catch (Exception ex) { statusLabel.Text = "Не удалось сохранить коллекцию: " + ex.Message; }
        }
    }

    private void OpenCollectionClicked(object sender, RoutedEventArgs e)
    {
        if (_busy || !ConfirmReplaceCollection()) return;
        var dialog = new OpenFileDialog { Title = "Открыть коллекцию", Filter = "Коллекция Tessera|*.ttiles" };
        if (dialog.ShowDialog(this) != true) return;
        try { LoadCollectionFromFile(dialog.FileName); }
        catch (Exception ex) { statusLabel.Text = "Не удалось открыть коллекцию: " + ex.Message; }
    }

    private void RemoveAssetClicked(object sender, RoutedEventArgs e)
    {
        if (_busy || ActiveAsset is not { } asset) return;
        FinishStroke();
        if (asset.HasLocalEdits && TesseraDialog.Show(this, "Убрать тайл с локальными правками из коллекции?", "Убрать ассет", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        int index = _assets.IndexOf(asset);
        _assets.Remove(asset);
        assetList.SelectedItem = _assets.Count == 0 ? null : _assets[Math.Min(index, _assets.Count - 1)];
        _historyAsset = ActiveAsset;
        RefreshActiveAsset(); ConfigureWatchers(); SetProjectDirty();
    }

    public void ExportActivePng(string path)
    {
        if (ActiveAsset is not { } asset) throw new InvalidOperationException("Выберите тайл.");
        FinishStroke();
        path = Path.GetFullPath(path);
        if (_assets.Any(item => string.Equals(item.SourcePath, path, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(_projectPath, path, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Выберите новый PNG: исходники и проект не перезаписываются.");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temporary)) asset.WritePng(stream);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void ExportClicked(object sender, RoutedEventArgs e)
    {
        if (_busy || ActiveAsset is not { } asset) return;
        var dialog = new SaveFileDialog { Title = "Экспорт тайла", Filter = "PNG|*.png", DefaultExt = ".png", AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(asset.Name) + "-edited.png" };
        if (dialog.ShowDialog(this) != true) return;
        try { ExportActivePng(dialog.FileName); statusLabel.Text = "PNG сохранён · " + Path.GetFileName(dialog.FileName); }
        catch (Exception ex) { statusLabel.Text = "Не удалось экспортировать PNG: " + ex.Message; }
    }
}
