using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

public partial class ImageEditorPage
{
    internal Bitmap WorkspaceBasis() => ProcessingBasis == 1 && _downscaledResult is not null ? _downscaledResult : _sourceImage!;

    internal string WorkspaceBasisPath() => ProcessingBasis == 1 && _activeSource!.SelectedGeneration is { } selected ? Host._library.ResultPath(_activeSource, selected) : Host._library.SourcePath(_activeSource!);

    internal void SaveWorkspaceImageResult(Bitmap result, string operation, SourceEntry target, Guid? parentId, EditorSettings settings, ProcessingStep? step = null, bool manual = false)
    {
        if (!Host._sources.Contains(target)) throw new InvalidOperationException(Localization.Text("TheSourceAssetIsNoLonger"));
        var entry = Host._library.SaveGeneration(target, result, null, new GenerationEntry
        {
            Operation = operation,
            Caption = operation,
            ParentGenerationId = parentId,
            PreservesTransparency = true,
            Settings = settings,
            OperationId = manual ? "manual.erase" : step?.OperationId,
            AppliedRecipe = step is null || manual ? null : HistoryRecipes.Single(step),
            RecipeUnavailableReason = manual ? Localization.Text("TheChainIncludesManualErasing") : step is null ? Localization.Text("OperationParametersWereNotSaved") : null
        });
        if (!History.CommitGeneration(target, entry)) return;
        if (target == _activeSource) ProcessingBasis = 1;
        Host.SynchronizeWorkspace();
    }

    internal void ReleaseImageEditor()
    {
        History.RememberTrayScroll();
        History.WithSelectionPaused(() =>
        {
            History.ReleaseDocument(_activeSource);
            _activeSource = null;
            _selectedFilePath = null;
            Preview.ClearLargeImageApprovals();
            Info.ResetInfoSnapshot();
            Preview.ClearSourceImages();
            _sourceImage?.Dispose();
            _sourceImage = null;
            ClearDisplayedResult();
            Preview.UpdateComparisonPreview();
        });
    }

    private void RestoreWorkspaceImageView(SourceEntry source)
    {
        if (!Host._workspaceReady || Host._workspaceCatalog?.State.Assets.FirstOrDefault(a => a.Id == source.Id) is not { } asset) return;
        if (source.DraftSettings is { } draft) Tools.RestoreSettings(draft);
        ProcessingBasis = asset.Basis == 1 && _downscaledResult is not null ? 1 : 0;
        Preview.PreviewZoom = Math.Clamp(asset.Zoom, 0, EditorPreview.ZoomFactors.Length - 1);
        Preview.PreviewBackground = Math.Clamp(asset.Background, 0, 3);
    }
}
