using System.Windows;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class MainWindow
{
    private readonly Dictionary<string, MaterialMapsWindow> _materialMapWindows = [];

    private async Task OpenMaterialMapsAsync()
    {
        if (!ImageEditorVisible || WorkspaceBusy || _activeSource is null) return;
        bool result = ProcessingBasis == 1 && _downscaledResult is not null;
        string key = _activeSource.Id.ToString("N") + ":" + (result ? _activeSource.SelectedGeneration?.Id.ToString("N") : "source");
        if (_materialMapWindows.TryGetValue(key, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate(); return;
        }
        using var image = (Bitmap)(result ? _downscaledResult! : _sourceImage!).Clone();
        var window = new MaterialMapsWindow { Owner = this, ValidateExportPath = EnsureExportPathAllowed };
        _materialMapWindows.Add(key, window);
        window.Closed += (_, _) => _materialMapWindows.Remove(key);
        window.Show();
        try { await window.SetImageAsync(image, _activeSource.Label); }
        catch { window.Close(); throw; }
    }
}
