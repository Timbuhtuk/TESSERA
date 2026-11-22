using System.IO;
using Microsoft.Win32;

namespace Tessera.Desktop;

public partial class MainWindow
{
    private async Task ExtractLegacyTileArchiveAsync(string path, string? name = null)
    {
        var dialog = new OpenFolderDialog { Title = Localization.Text("WhereShouldTesseraExtractPngsTtilesConfirmation") };
        if (dialog.ShowDialog(this) != true) return;
        name = LegacyTileArchive.FileName(name ?? Path.GetFileNameWithoutExtension(path));
        string stem = Path.Combine(dialog.FolderName, Localization.Text("PngsFrom") + " " + (name.Length == 0 ? "tiles" : name));
        string destination = stem;
        for (int q = 2; Directory.Exists(destination) || File.Exists(destination); q++)
            destination = $"{stem} ({q})";
        Editor.SetProcessingState(true);
        try
        {
            EnsureExportPathAllowed(destination);
            int count = await Task.Run(() => LegacyTileArchive.Extract(path, destination));
            statusLabel.Text = count == 0 ? Localization.Text("TheTtilesArchiveIsEmpty") :
                Localization.Format("ExtractedPNGFilesFormat", count, destination);
        }
        catch (Exception ex) { WorkspaceError(ex); }
        finally { Editor.SetProcessingState(false); }
    }

    internal async Task OpenAtlasFromCommandLineAsync(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            await ImportWorkspaceFilesAsync([full]);
            if (LegacyTileArchive.IsArchive(full)) return;
            var source = _sources.FirstOrDefault(item => string.Equals(item.OriginalPath, full, StringComparison.OrdinalIgnoreCase));
            if (source is null || _workspaceCatalog?.State.Assets.FirstOrDefault(item => item.Id == source.Id) is not { } asset) return;
            await OpenWorkspaceAssetAsync(asset);
            if (Editor._activeSource?.Id == source.Id) Editor.OpenAtlasTool();
        }
        catch (Exception ex) { WorkspaceError(ex); }
    }
}
