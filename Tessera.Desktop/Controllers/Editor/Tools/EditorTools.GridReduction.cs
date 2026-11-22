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

internal sealed partial class EditorTools
{
    internal void InitializeGridReduction() => _editor.compactGridButton.Click += CompactGrid;

    private int CompactableCellSize => _editor._activeSource?.SelectedGeneration is { } generation
        ? generation.IsGlobalAlignment ? generation.CellSize : 0
        : _editor._activeSource?.AlignedCellSize ?? 0;

    internal void UpdateCompactButton()
    {
        int cellSize = CompactableCellSize;
        var image = _editor._activeSource?.SelectedGeneration is null ? _editor._sourceImage : _editor._downscaledResult;
        _editor.compactGridButton.IsEnabled = !_editor._processing && image is not null && cellSize >= 2;
        _editor.compactGridButton.ToolTip = _editor._activeSource?.SelectedGeneration is { IsAlignment: true, IsGlobalAlignment: false }
            ? Localization.Text("ReducingCellsOnePixelRequiresShared")
            : image is not null && cellSize >= 2
            ? Localization.Format("OneCellOnePixelFormat", cellSize, cellSize, (image.Width - 1) / cellSize + 1, (image.Height - 1) / cellSize + 1)
            : Localization.Text("SelectAlignedResultOrSourceCreated");
    }

    private async void CompactGrid(object? sender, RoutedEventArgs e)
    {
        if (_editor._processing || _editor._activeSource is null || CompactableCellSize < 2) return;
        var document = _editor._activeSource;
        var parent = document.SelectedGeneration;
        int cellSize = CompactableCellSize;
        var input = parent is null ? _editor._sourceImage : _editor._downscaledResult;
        if (input is null) return;
        var settings = parent?.Settings ?? CaptureSettings();
        _editor.SetProcessingState(true);
        Host.statusLabel.Text = Localization.Format("ReducingGridCellOnePixelAction", cellSize, cellSize);
        try
        {
            using var source = (Bitmap)input.Clone();
            var generation = await Task.Run(() =>
            {
                using var compact = PixelGridReducer.Reduce(source, cellSize);
                return Host._library.SaveGeneration(document, compact, null, new GenerationEntry
                {
                    OperationId = ProcessingOperations.Reduce,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = ProcessingOperations.Reduce, CellSize = cellSize, UsePreviousAlignment = parent?.IsAlignment == true && parent.Settings.DetectGrid }),
                    Settings = settings,
                    IsGridReduction = true,
                    PreservesTransparency = true,
                    ParentGenerationId = parent?.Id,
                    CellSize = cellSize,
                    Caption = Localization.Format("GRIDTO1PxFromPxFormat", compact.Width, compact.Height, cellSize),
                    Log = Localization.Format("OneCellPerPixelPartialEdgeFormat", source.Width, source.Height, compact.Width, compact.Height)
                });
            });
            if (!_editor.History.CommitGeneration(document, generation)) return;
            Host.Notify(Localization.Format("GridReductionSaved", generation.Width, generation.Height), NotificationKind.Success);
        }
        catch (Exception ex)
        {
            Host._mainProgress?.Dispose();
            Host.Notify(Localization.Format("CouldNotReduceGridFormat", ex.Message), NotificationKind.Error);
        }
        finally { _editor.SetProcessingState(false); }
    }
}
