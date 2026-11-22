using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Tessera.Core.Alignment;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

internal sealed partial class EditorHistory
{
    internal void InitializeDrag()
    {
        foreach (var origin in new FrameworkElement[] { _editor.generationStrip, _editor.resultPreview })
        {
            origin.PreviewMouseLeftButtonDown += (_, e) =>
            {
                _dragCandidate = null;
                if (_editor._processing || _editor._activeSource is null || ImageEditorPage.Within<ScrollBar>(e.OriginalSource as DependencyObject) is not null ||
                    ImageEditorPage.Within<ButtonBase>(e.OriginalSource as DependencyObject) is not null) return;
                if (ReferenceEquals(origin, _editor.resultPreview) && _editor.Preview.ContourPreviewVisible) return;
                var generation = ReferenceEquals(origin, _editor.generationStrip)
                    ? (ItemsControl.ContainerFromElement(_editor.generationStrip, e.OriginalSource as DependencyObject) as ListBoxItem)?.Content as GenerationEntry
                    : _editor._activeSource.SelectedGeneration;
                if (generation is null) return;
                _dragCandidate = new GenerationReference(_editor._activeSource.Id, generation.Id);
                _dragStart = e.GetPosition(Host);
            };
            origin.PreviewMouseLeftButtonUp += (_, _) => _dragCandidate = null;
            origin.PreviewMouseMove += (_, e) =>
            {
                if (_editor._processing || _dragCandidate is null || e.LeftButton != MouseButtonState.Pressed) return;
                var position = e.GetPosition(Host);
                if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                var reference = _dragCandidate;
                _dragCandidate = null;
                var source = Host._sources.First(s => s.Id == reference.SourceId);
                var generation = source.Generations.First(g => g.Id == reference.GenerationId);
                var data = CreateGenerationDragData(source, generation);
                DragDrop.DoDragDrop(origin, data, DragDropEffects.Copy | DragDropEffects.Move);
                e.Handled = true;
            };
        }
    }

    internal void ClearDrag() => _dragCandidate = null;

    internal const string GenerationDragFormat = "Pixelizator.Generation";

    private sealed record GenerationReference(Guid SourceId, Guid GenerationId);

    private GenerationReference? _dragCandidate;

    private Point _dragStart;

    private DataObject CreateGenerationDragData(SourceEntry source, GenerationEntry generation)
        => new(GenerationDragFormat, $"{source.Id:N}:{generation.Id:N}");

    internal bool TryGetDraggedGeneration(IDataObject data, out SourceEntry source, out GenerationEntry generation)
    {
        source = null!; generation = null!;
        if (!data.GetDataPresent(GenerationDragFormat) || data.GetData(GenerationDragFormat) is not string text) return false;
        var parts = text.Split(':');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var sourceId) || !Guid.TryParseExact(parts[1], "N", out var generationId)) return false;
        source = Host._sources.FirstOrDefault(s => s.Id == sourceId)!;
        generation = (source is null ? null : Host._library.FindGeneration(source, generationId))!;
        return source is not null && generation is not null;
    }

    internal bool IsSourceDropTarget(DependencyObject? target)
    {
        for (var current = target; current is not null; current = ImageEditorPage.ParentOf(current))
            if (ReferenceEquals(current, _editor.sourcePane) || ReferenceEquals(current, _editor.sourceTray)) return true;
        return false;
    }

    internal async Task AddGenerationAsSourceAsync(SourceEntry source, GenerationEntry generation)
    {
        if (_editor._processing) return;
        _editor.SetProcessingState(true);
        try
        {
            var existing = Host._sources.ToArray();
            var imported = await Task.Run(() => Host._library.ImportGeneration(source, generation, existing));
            if (!Host._sources.Contains(imported)) Host._sources.Add(imported);
            if (Host._workspaceReady && Host._workspaceCatalog!.State.Assets.FirstOrDefault(a => a.Id == imported.Id) is { } asset) Host.AddNewWorkspaceAsset(asset);
            _editor.SelectSource(imported);
            Host.statusLabel.Text = Localization.Text("ResultAddedAsASeparateSource");
        }
        catch (Exception ex) { Host.statusLabel.Text = Localization.Format("CouldNotAddResultFormat", ex.Message); }
        finally { _editor.SetProcessingState(false); }
    }
}
