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

    private void UpdateProjectTitle() => Title = Localization.Format("Проверка тайлинга · {0}{1}", (_projectPath is null ? "TESSERA" : Path.GetFileName(_projectPath)), (_projectDirty ? " *" : ""));

    public void SaveCollectionToFile(string path)
    {
        if (_busy) throw new InvalidOperationException(Localization.Text("Дождитесь сглаживания."));
        if (WorkspaceSavePath is null || !string.Equals(Path.GetFullPath(path), Path.GetFullPath(WorkspaceSavePath), StringComparison.OrdinalIgnoreCase))
            ValidateSharedExportPath(Path.GetFullPath(path));
        FinishStroke();
        var settings = new TileCollectionSettings
        {
            Selected = ActiveAsset?.Id, Companion = CompanionAsset?.Id,
            RepeatX = repeatXInput.IsChecked == true, RepeatY = repeatYInput.IsChecked == true,
            Copies = copiesInput.SelectedIndex, Zoom = Math.Clamp(zoomInput.SelectedIndex - 4, 0, 6),
            ZoomScale = TileZoomFactors[Math.Max(0, zoomInput.SelectedIndex)], Background = backgroundInput.SelectedIndex,
            Heat = heatInput.IsChecked == true, Guides = guidesInput.IsChecked == true,
            Threshold = thresholdInput.Value, Opacity = opacityInput.Value, Stripe = stripeInput.Value,
            Color = paintColorInput.Text, BrushSize = brushSizeInput.SelectedIndex, PaintOpacity = paintOpacityInput.Value,
            Passes = int.TryParse(smoothPassesInput.Text, out int passes) ? Math.Clamp(passes, 1, 100) : 1,
            SmoothEdgesOnly = smoothEdgesInput.IsChecked == true
        };
        TileCollectionFile.Save(path, _assets, settings, _protectedSourcePaths);
        if (WorkspaceSavePath is { } workspacePath && !string.Equals(Path.GetFullPath(workspacePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) WorkspaceSavePath = null;
        _projectPath = Path.GetFullPath(path); _projectDirty = false;
        UpdateProjectTitle(); statusLabel.Text = Localization.Text("Коллекция и правки сохранены · ") + Path.GetFileName(path);
        WorkspaceSaved?.Invoke();
    }

    public void LoadCollectionFromFile(string path)
    {
        if (_busy) throw new InvalidOperationException(Localization.Text("Дождитесь сглаживания."));
        // Read the complete project before changing the current session.
        var loaded = TileCollectionFile.LoadWithSourcePaths(path);
        WorkspaceSavePath = null;
        FinishStroke(); _loadVersion++;
        _reloadTimer.Stop(); _changedPaths.Clear();
        _settingCollection = true;
        try
        {
            foreach (string source in loaded.SourcePaths) _protectedSourcePaths.Add(source);
            _assets.Clear();
            foreach (var asset in loaded.Assets) _assets.Add(asset);
            var s = loaded.Settings;
            assetList.SelectedItem = _assets.FirstOrDefault(asset => asset.Id == s.Selected) ?? _assets.FirstOrDefault();
            repeatXInput.IsChecked = s.RepeatX; repeatYInput.IsChecked = s.RepeatY;
            copiesInput.SelectedIndex = Math.Clamp(s.Copies, 0, 2);
            int zoomIndex = s.ZoomScale is { } scale ? Array.IndexOf(TileZoomFactors, scale) : -1;
            // Version-1 projects originally stored indexes for Fit and 100–3200% only.
            zoomInput.SelectedIndex = zoomIndex >= 0 ? zoomIndex : s.Zoom > 0 ? Math.Clamp(s.Zoom, 1, 6) + 4 : 0;
            backgroundInput.SelectedIndex = Math.Clamp(s.Background, 0, 3);
            heatInput.IsChecked = s.Heat; guidesInput.IsChecked = s.Guides;
            thresholdInput.Value = Finite(s.Threshold, 0, 30, 5);
            opacityInput.Value = Finite(s.Opacity, 0, 100, 70);
            stripeInput.Value = Finite(s.Stripe, 1, 8, 1);
            paintColorInput.Text = s.Color ?? "#FFFFFFFF";
            brushSizeInput.SelectedIndex = Math.Clamp(s.BrushSize, 0, 2);
            paintOpacityInput.Value = Finite(s.PaintOpacity, 0, 100, 100);
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
        statusLabel.Text = Localization.Format("Коллекция открыта · {0} ассетов", _assets.Count);
    }

    private static double Finite(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private bool SaveCollectionWithDialog()
    {
        var dialog = new SaveFileDialog
        {
            Title = Localization.Text("Сохранить коллекцию тайлов"), Filter = Localization.Text("Коллекция Tessera|*.ttiles"), DefaultExt = ".ttiles",
            AddExtension = true, FileName = _projectPath is null ? "tiles.ttiles" : Path.GetFileName(_projectPath),
            InitialDirectory = _projectPath is null ? "" : Path.GetDirectoryName(_projectPath)
        };
        if (dialog.ShowDialog(this) != true) return false;
        try { SaveCollectionToFile(dialog.FileName); return true; }
        catch (Exception ex) { statusLabel.Text = Localization.Text("Не удалось сохранить коллекцию: ") + ex.Message; return false; }
    }

    private bool ConfirmReplaceCollection()
    {
        FinishStroke();
        if (_allowClose || !_projectDirty) return true;
        var answer = TesseraDialog.Show(this, Localization.Text("Сохранить изменения коллекции перед закрытием или открытием другого проекта?"), Localization.Text("Коллекция тайлов"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer == MessageBoxResult.No || answer == MessageBoxResult.Yes && SaveCollectionWithDialog();
    }

    private void ConfirmWindowClosing(object? sender, CancelEventArgs e)
    {
        if (e.Cancel) return;
        if (_busy) { e.Cancel = true; statusLabel.Text = Localization.Text("Дождитесь завершения сглаживания."); return; }
        if (!ConfirmReplaceCollection()) e.Cancel = true;
    }

    public void DiscardAndClose() { _allowClose = true; Close(); }

    private void SaveCollectionClicked(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (WorkspaceSavePath is { } workspacePath) { try { SaveCollectionToFile(workspacePath); } catch (Exception ex) { statusLabel.Text = ex.Message; } return; }
        if (_projectPath is null || sender is System.Windows.Controls.Button) SaveCollectionWithDialog();
        else
        {
            try { SaveCollectionToFile(_projectPath); }
            catch (Exception ex) { statusLabel.Text = Localization.Text("Не удалось сохранить коллекцию: ") + ex.Message; }
        }
    }

    private void OpenCollectionClicked(object sender, RoutedEventArgs e)
    {
        if (_busy || !ConfirmReplaceCollection()) return;
        var dialog = new OpenFileDialog { Title = Localization.Text("Открыть коллекцию"), Filter = Localization.Text("Коллекция Tessera|*.ttiles") };
        if (dialog.ShowDialog(this) != true) return;
        try { LoadCollectionFromFile(dialog.FileName); }
        catch (Exception ex) { statusLabel.Text = Localization.Text("Не удалось открыть коллекцию: ") + ex.Message; }
    }

    private void RemoveAssetClicked(object sender, RoutedEventArgs e)
    {
        if (_busy || ActiveAsset is not { } asset) return;
        FinishStroke();
        if (asset.HasLocalEdits && TesseraDialog.Show(this, Localization.Text("Убрать тайл с локальными правками из коллекции?"), Localization.Text("Убрать ассет"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        int index = _assets.IndexOf(asset);
        _assets.Remove(asset);
        assetList.SelectedItem = _assets.Count == 0 ? null : _assets[Math.Min(index, _assets.Count - 1)];
        _historyAsset = ActiveAsset;
        RefreshActiveAsset(); ConfigureWatchers(); SetProjectDirty();
    }

    public void ExportActivePng(string path)
    {
        if (ActiveAsset is not { } asset) throw new InvalidOperationException(Localization.Text("Выберите тайл."));
        FinishStroke();
        path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            throw new IOException(Localization.Text("Сохраните тайл в формате PNG."));
        ValidateSharedExportPath(path);
        if (_protectedSourcePaths.Contains(path) ||
            string.Equals(_projectPath, path, StringComparison.OrdinalIgnoreCase))
            throw new IOException(Localization.Text("Выберите новый PNG: исходники и проект не перезаписываются."));
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
        var dialog = new SaveFileDialog { Title = Localization.Text("Экспорт тайла"), Filter = "PNG|*.png", DefaultExt = ".png", AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(asset.Name) + "-edited.png" };
        if (dialog.ShowDialog(this) != true) return;
        try { ExportActivePng(dialog.FileName); statusLabel.Text = Localization.Text("PNG сохранён · ") + Path.GetFileName(dialog.FileName); }
        catch (Exception ex) { statusLabel.Text = Localization.Text("Не удалось экспортировать PNG: ") + ex.Message; }
    }

    private void ValidateSharedExportPath(string path)
    {
        try { ValidateExportPath?.Invoke(path); }
        catch (InvalidOperationException ex) { throw new IOException(ex.Message, ex); }
    }
}
