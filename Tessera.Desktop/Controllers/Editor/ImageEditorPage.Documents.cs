using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tessera.Core.Imaging;
using Tessera.Core.Alignment;

namespace Tessera.Desktop;

public partial class ImageEditorPage
{
    internal SourceEntry? _activeSource;

    private void LoadImage(string path)
    {
        var source = Host._library.Import(path, Host._sources);
        if (!Host._sources.Contains(source)) Host._sources.Add(source);
        SelectSource(source);
    }

    internal void SelectSource(SourceEntry source)
    {
        Host.AdoptWorkspaceSource(source);
        History.ChooseSourceFolder(source);
        if (ReferenceEquals(_activeSource, source)) { ProcessingBasis = 0; SyncProcessingSelection(); Host.ShowEditor(); return; }
        OpenSourceImage(source);
    }

    internal void OpenSourceImage(SourceEntry source)
    {
        var bitmap = ImageLibrary.ReadBitmap(Host._library.SourcePath(source));
        var previous = _activeSource;
        Host.ShowEditor();
        if (_activeSource is not null && _activeSource != source) _activeSource.DraftSettings = Tools.CaptureSettings();
        History.WithSelectionPaused(() =>
        {
            sourcePreview.Image = null;
            _sourceImage?.Dispose();
            _sourceImage = bitmap;
            _activeSource = source;
            _selectedFilePath = source.OriginalPath;
            ClearDisplayedResult();
            sourceStrip.SelectedItem = source;
            sourceStrip.ScrollIntoView(source);
            History.ConfigureHistorySource(source);
            if (previous != source) previous?.UnloadHistory();
            Host.fileLabel.Text = source.Label;
            Host.fileLabel.ToolTip = source.OriginalPath;
        });
        if (source.SelectedGeneration is { } selected) SelectGeneration(selected);
        else
        {
            if (source.DraftSettings is { } draft) Tools.RestoreSettings(draft);
            else if (aspectLock.IsChecked == true) Tools.SizeChangedByUser(widthChanged: true);
            Preview.UpdateSourcePreview();
            Tools.UpdateSizeHint();
            Host.statusLabel.Text = Localization.Text("SourceSelected");
        }
        sourcePreview.ScrollToRelativePosition(0, 0);
        resultPreview.ScrollToRelativePosition(0, 0);
        ProcessingBasis = 0;
        SyncProcessingSelection();
        SetProcessingState(_processing);
        RestoreWorkspaceImageView(source);
    }

    internal void ClearDisplayedResult()
    {
        Preview.ClearDisplayedLivePreview();
        resultPreview.Image = null;
        _downscaledResult?.Dispose(); _downscaledResult = null;
        _resultStale = _resultPreservesTransparency = _resultIsAlignment = false;
        resultCaption.Text = Localization.Text("Result");
        processingLog.Clear();
    }

    internal void SelectGeneration(GenerationEntry generation)
    {
        if (_activeSource is null || !_activeSource.Generations.Contains(generation)) return;
        Preview.ClearDisplayedLivePreview();
        var output = ImageLibrary.ReadBitmap(Host._library.ResultPath(_activeSource, generation));
        if (generation.OperationId != ProcessingOperations.Macro) Tools.RestoreSettings(generation.Settings);
        History.WithSelectionPaused(() =>
        {
            sourcePreview.Image = null;
            resultPreview.Image = null;
            _downscaledResult?.Dispose(); _downscaledResult = output;
            resultPreview.Image = output;
            _resultIsAlignment = generation.IsAlignment;
            _resultPreservesTransparency = generation.PreservesTransparency;
            _resultStale = false;
            resultCaption.Text = Localization.StoredText(generation.Caption);
            processingLog.Text = Localization.StoredText(generation.Log);
            _activeSource.SelectedGeneration = generation;
            generationStrip.SelectedItem = generation;
            generationStrip.ScrollIntoView(generation);
        });
        ProcessingBasis = 1;
        SyncProcessingSelection();
        Preview.UpdateSourcePreview();
        Tools.UpdateSizeHint();
        SetProcessingState(_processing);
        Host.statusLabel.Text = "";
    }

    internal void SyncProcessingSelection() => History.SelectProcessingBasis(_activeSource, ProcessingBasis);

    internal void RemoveSource(SourceEntry source)
    {
        int index = Host._sources.IndexOf(source);
        if (index < 0) return;
        foreach (var entry in Host._materialMapWindows.Where(entry => entry.Key.Source == source.Id).ToArray())
        {
            entry.Value.Close();
            if (Host._materialMapWindows.ContainsKey(entry.Key))
                throw new InvalidOperationException(Localization.Text("SaveCloseMaterialMapsBeforeDeleting"));
        }
        bool selected = ReferenceEquals(_activeSource, source);
        var visible = (Host._workspaceReady ? Host._workspaceSources : Host._sources).ToList();
        int visibleIndex = visible.IndexOf(source);
        var next = visible.Count <= 1 || visibleIndex < 0 ? null : visible[visibleIndex + 1 < visible.Count ? visibleIndex + 1 : visibleIndex - 1];
        Host._library.RemoveSource(source);
        History.WithSelectionPaused(() =>
        {
            Host._sources.Remove(source);
            if (selected)
            {
                ClearDisplayedResult();
                sourcePreview.Image = null;
                _sourceImage?.Dispose(); _sourceImage = null;
                _activeSource = null;
                _selectedFilePath = null;
                sourceStrip.SelectedItem = null;
                generationStrip.ItemsSource = null;
                History.DetachCurrentHistory();
                History.RefreshHistoryLayout();
                Host.fileLabel.Text = Localization.Text("OpenAnImage");
                Host.fileLabel.ToolTip = null;
                Preview.UpdateSourcePreview();
                Tools.UpdateSizeHint();
            }
        });
        if (selected && next is not null) SelectSource(next);
        else if (selected && Host._workspaceReady) { Host._workspaceActive = null; Host.ShowHome(); }
        Host.statusLabel.Text = Host._sources.Count == 0
            ? Localization.Text("AllSourcesRemovedFromTheApp")
            : Localization.Text("SourceItsResultsRemovedApp");
    }
}
