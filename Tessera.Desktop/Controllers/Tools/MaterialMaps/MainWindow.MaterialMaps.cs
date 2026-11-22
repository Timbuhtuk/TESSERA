using System.Windows;
using System.IO;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

public partial class MainWindow
{
    internal readonly Dictionary<(Guid Source, Guid? StandaloneGeneration), MaterialMapsWindow> _materialMapWindows = [];

    private Task OpenMaterialMapsAsync() => ShowMaterialMapsAsync(rebuild: false);

    private Task RebuildMaterialMapsAsync() => ShowMaterialMapsAsync(rebuild: true);

    private async Task ShowMaterialMapsAsync(bool rebuild)
    {
        if (!Editor.ImageEditorVisible || WorkspaceBusy || Editor._activeSource is not { } source) return;
        bool result = Editor.ProcessingBasis == 1 && Editor._downscaledResult is not null;
        Guid? generationId = result ? source.SelectedGeneration?.Id : null;
        var catalog = _workspaceCatalog;
        var key = (source.Id, catalog is null ? generationId : null);
        _materialMapWindows.TryGetValue(key, out var existing);
        var session = catalog?.FindMaterialSession(source.Id);
        string? savedPath = session is null ? null : catalog!.MaterialSessionPath(session);
        bool saved = File.Exists(savedPath);
        rebuild &= saved;
        if (rebuild && existing?.IsBusy == true)
            throw new InvalidOperationException(Localization.Text("WaitForTheMapsToFinish"));
        if (rebuild && TesseraDialogs.Show(this, Localization.Text("RebuildMapsFromTheCurrentVersion"),
            Localization.Text("MaterialMaps"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        using var image = rebuild || (!saved && existing is null) ? (Bitmap)(result ? Editor._downscaledResult! : Editor._sourceImage!).Clone() : null;

        if (existing is not null)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            if (rebuild && existing.CanLeave()) await ReplaceMapsAsync(existing, session!);
            existing.Activate();
            return;
        }
        var window = new MaterialMapsWindow { Owner = this, ValidateExportPath = EnsureExportPathAllowed };
        if (catalog is not null)
        {
            var storedSession = session ?? new WorkspaceMaterialSession { SourceId = source.Id, GenerationId = generationId };
            session = storedSession;
            bool firstSave = !saved;
            window.EnableWorkspace(document =>
            {
                catalog.SaveMaterialDocument(storedSession, document, firstSave ? generationId : storedSession.GenerationId);
                firstSave = false;
                RefreshMaterialMapStatus();
            });
            window.SetWorkspaceBasis(MaterialBasisLabel(source, saved ? storedSession.GenerationId : generationId));
            if (catalog.State.Assets.Any(a => a.Id == source.Id && a.Unreal is not null))
                window.SaveToUnreal = document => SaveUnrealMaps(storedSession, document);
        }
        _materialMapWindows.Add(key, window);
        window.Closed += (_, _) => _materialMapWindows.Remove(key);
        window.Show();
        try
        {
            if (saved)
            {
                await window.LoadDocumentAsync(savedPath!, source.Label);
                if (rebuild) await ReplaceMapsAsync(window, session!);
            }
            else
            {
                await window.SetImageAsync(image!, source.Label);
                window.SaveWorkspace();
            }
        }
        catch { if (window.Document is null) window.Close(); throw; }

        async Task ReplaceMapsAsync(MaterialMapsWindow target, WorkspaceMaterialSession storedSession)
        {
            await target.ReplaceWorkspaceDocumentAsync(image!, document =>
                catalog!.SaveMaterialDocument(storedSession, document, generationId, rebuild: true));
            target.SetWorkspaceBasis(MaterialBasisLabel(source, generationId));
            RefreshMaterialMapStatus();
        }
    }

    private static string MaterialBasisLabel(SourceEntry source, Guid? generationId) => generationId is { } id
        ? source.Generations.FirstOrDefault(generation => generation.Id == id)?.Label ?? Localization.Format("NamedResult", id.ToString("N")[..8])
        : Localization.Text("SourceLabel");

    private void RefreshMaterialMapStatus()
    {
        if (_workspaceBrowser?.IsVisible == true) _workspaceBrowser.Refresh();
    }
}
