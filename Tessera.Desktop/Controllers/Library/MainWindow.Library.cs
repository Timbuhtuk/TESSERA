using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tessera.Core.Imaging;
using Tessera.Core.Alignment;

namespace Tessera.Desktop;

public partial class MainWindow
{
    internal readonly ImageLibrary _library;

    internal readonly ObservableCollection<SourceEntry> _sources = [];

    private void InitializeLibrary(bool selectLatest)
    {
        Editor.History.InitializeSourceTrays();
        Editor.Preview.InitializeScrollSync();
        PreviewDragOver += (sender, e) =>
        {
            if (Editor.Dock.HandleToolDrop(e, commit: false)) return;
            if (Editor.Atlas.HandleAtlasTileDrop(e, commit: false)) return;
            if (_workspaceBrowser?.Visibility == Visibility.Visible) return;
            if (IsMoodboardDropTarget(e.OriginalSource as DependencyObject))
            {
                e.Effects = CanDropMoodboard(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true; return;
            }
            if (IsAsepriteDropTarget(e.OriginalSource as DependencyObject, e.Data))
            {
                e.Effects = CanDropAseprite(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true; return;
            }
            if (IsBackgroundDropTarget(e.OriginalSource as DependencyObject))
            {
                e.Effects = CanDropBackground(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true; return;
            }
            if (IsIconDropTarget(e.OriginalSource as DependencyObject))
            {
                e.Effects = CanDropIcon(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
                return;
            }
            if (Editor.History.HistoryDrop(e, commit: false)) return;
            bool supported = e.Data.GetDataPresent(EditorHistory.GenerationDragFormat)
                ? Editor.History.TryGetDraggedGeneration(e.Data, out _, out _) && Editor.History.IsSourceDropTarget(e.OriginalSource as DependencyObject)
                : DropPaths(e).Any(IsSupportedImage);
            e.Effects = !Editor._processing && supported ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        PreviewDrop += async (_, e) =>
        {
            if (Editor.Dock.HandleToolDrop(e, commit: true)) return;
            if (Editor.Atlas.HandleAtlasTileDrop(e, commit: true)) return;
            if (_workspaceBrowser?.Visibility == Visibility.Visible) return;
            if (Editor.History.HistoryDrop(e, commit: true)) return;
            e.Handled = true;
            if (_workspaceReady && !IsMoodboardDropTarget(e.OriginalSource as DependencyObject) && e.Data.GetData(DataFormats.FileDrop) is string[] workspacePaths)
            {
                await ImportWorkspaceFilesAsync(workspacePaths);
                if (_workspaceCatalog?.State.SelectedAsset is { } selectedId &&
                    _workspaceCatalog.State.Assets.FirstOrDefault(a => a.Id == selectedId) is { } selectedAsset)
                    await OpenWorkspaceAssetAsync(selectedAsset);
                return;
            }
            if (IsMoodboardDropTarget(e.OriginalSource as DependencyObject)) { await DropMoodboardAsync(e); return; }
            if (IsAsepriteDropTarget(e.OriginalSource as DependencyObject, e.Data)) { await DropAsepriteAsync(e); return; }
            if (IsBackgroundDropTarget(e.OriginalSource as DependencyObject)) { await DropBackgroundAsync(e); return; }
            if (IsIconDropTarget(e.OriginalSource as DependencyObject)) { await DropIconAsync(e); return; }
            if (Editor._processing) { e.Effects = DragDropEffects.None; return; }
            if (e.Data.GetDataPresent(EditorHistory.GenerationDragFormat))
            {
                if (Editor.History.IsSourceDropTarget(e.OriginalSource as DependencyObject) && Editor.History.TryGetDraggedGeneration(e.Data, out var source, out var generation))
                {
                    e.Effects = DragDropEffects.Copy;
                    await Editor.History.AddGenerationAsSourceAsync(source, generation);
                }
                else e.Effects = DragDropEffects.None;
                return;
            }
            string[] paths = DropPaths(e);
            if (paths.Length > 0) await AddImagesAsync(paths);
        };
        try
        {
            foreach (var source in _library.LoadHeaders()) _sources.Add(source);
            if (selectLatest && _sources.Count > 0) Editor.SelectSource(_sources[^1]);
            if (_library.LoadWarnings.Count > 0)
                Notify(Localization.Format("HistoryOpenedFormat", _library.LoadWarnings.Count), NotificationKind.Warning);
        }
        catch (Exception ex) { Notify(Localization.Format("CouldNotLoadHistoryFormat", ex.Message), NotificationKind.Error); }
    }

    private static string[] DropPaths(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths ? paths : [];

    private static bool IsSupportedImage(string path) => File.Exists(path) && Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";

    private async Task AddImagesAsync(IEnumerable<string> paths)
    {
        if (_workspaceReady)
        {
            await ImportWorkspaceFilesAsync(paths.ToArray());
            if (_workspaceCatalog?.State.Assets.FirstOrDefault(a => a.Id == _workspaceCatalog.State.SelectedAsset) is { } asset)
                await OpenWorkspaceAssetAsync(asset);
            return;
        }
        if (Editor._processing) return;
        string[] progressFiles = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        int progressDone = 0;
        Editor.SetProcessingState(true);
        var failures = new List<string>();
        SourceEntry? selected = null;
        int added = 0;
        try
        {
            foreach (string path in progressFiles)
            {
                try
                {
                    statusLabel.Text = Localization.Format("AddingAction", Path.GetFileName(path));
                    var existing = _sources.ToArray();
                    var source = await Task.Run(() => _library.Import(path, existing));
                    if (!_sources.Contains(source)) { _sources.Add(source); added++; }
                    selected = source;
                }
                catch (Exception ex) { failures.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
                CurrentProcessingProgress?.Report((double)++progressDone / progressFiles.Length);
            }
            if (selected is not null) Editor.SelectSource(selected);
            if (failures.Count == 0) CompleteProcessingProgress();
            string summary = failures.Count == 0
                ? Localization.Format("SourceImportSummary", added, _sources.Count)
                : Localization.Format("PartialImportSummary", added, failures.Count, failures[0]);
            Notify(summary, BatchNotificationKind(progressFiles.Length - failures.Count, failures.Count));
            statusLabel.ToolTip = failures.Count == 0 ? null : string.Join(Environment.NewLine, failures);
        }
        finally { Editor.SetProcessingState(false); }
    }
}
