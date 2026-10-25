using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PixelArtAlignment;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class MainWindow
{
    private static readonly double[] ZoomFactors = [0, .0625, .125, .25, .5, 1, 2, 4, 8, 16, 32, 64];
    private const string GenerationDragFormat = "Pixelizator.Generation";
    private sealed record GenerationReference(Guid SourceId, Guid GenerationId);
    private GenerationReference? _dragCandidate;
    private Point _dragStart;
    private int _zoomRequestRevision;

    private void InitializeImageActions()
    {
        compactGridButton.Click += CompactGrid;
        sourcePreview.ImageChanged += (_, _) => UpdateSharedPreviewScale();
        resultPreview.ImageChanged += (_, _) => UpdateSharedPreviewScale();
        foreach (var preview in new[] { sourcePreview, resultPreview })
            preview.PreviewMouseWheel += (_, e) =>
            {
                if (ChangePreviewZoom(preview, e.Delta, e.GetPosition(preview), Keyboard.Modifiers)) e.Handled = true;
            };
        _comparisonPreview.PreviewMouseWheel += (_, e) =>
        {
            if (HandleComparisonWheel(e.Delta, e.GetPosition(_comparisonPreview), Keyboard.Modifiers)) e.Handled = true;
        };
        foreach (var origin in new FrameworkElement[] { generationStrip, resultPreview })
        {
            origin.PreviewMouseLeftButtonDown += (_, e) =>
            {
                _dragCandidate = null;
                if (_processing || _activeSource is null || Within<ScrollBar>(e.OriginalSource as DependencyObject) is not null ||
                    Within<ButtonBase>(e.OriginalSource as DependencyObject) is not null) return;
                if (ReferenceEquals(origin, resultPreview) && ContourPreviewVisible) return;
                var generation = ReferenceEquals(origin, generationStrip)
                    ? (ItemsControl.ContainerFromElement(generationStrip, e.OriginalSource as DependencyObject) as ListBoxItem)?.Content as GenerationEntry
                    : _activeSource.SelectedGeneration;
                if (generation is null) return;
                _dragCandidate = new GenerationReference(_activeSource.Id, generation.Id);
                _dragStart = e.GetPosition(this);
            };
            origin.PreviewMouseLeftButtonUp += (_, _) => _dragCandidate = null;
            origin.PreviewMouseMove += (_, e) =>
            {
                if (_processing || _dragCandidate is null || e.LeftButton != MouseButtonState.Pressed) return;
                var position = e.GetPosition(this);
                if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                var reference = _dragCandidate;
                _dragCandidate = null;
                var source = _sources.First(s => s.Id == reference.SourceId);
                var generation = source.Generations.First(g => g.Id == reference.GenerationId);
                var data = CreateGenerationDragData(source, generation);
                DragDrop.DoDragDrop(origin, data, DragDropEffects.Copy | DragDropEffects.Move);
                e.Handled = true;
            };
        }
    }

    private bool HandleComparisonWheel(int delta, Point pointer, ModifierKeys modifiers)
    {
        if (_comparisonPreview.Image is null || delta == 0) return false;
        if ((modifiers & ModifierKeys.Control) != 0)
        {
            _comparisonPreview.ScrollByWheel(delta);
            return true;
        }
        return ChangePreviewZoom(_comparisonPreview, delta, pointer, modifiers);
    }

    private bool ChangePreviewZoom(PixelPreview preview, int delta, Point pointer, ModifierKeys modifiers)
    {
        bool comparison = ReferenceEquals(preview, _comparisonPreview);
        if (((modifiers & ModifierKeys.Control) == 0) != comparison || preview.Image is null || delta == 0) return false;
        var image = preview.Image;
        var anchor = preview.ImagePointAt(pointer);
        double factor = PreviewZoom > 0 ? ZoomFactors[PreviewZoom] : preview.EffectiveZoom;
        if (!comparison && PreviewZoom == 0 && preview.ReferenceImageSize is { } reference)
        {
            var referencePreview = sourcePreview.Image?.Size == reference ? sourcePreview : resultPreview;
            factor = referencePreview.EffectiveZoom;
        }
        int steps = Math.Max(1, Math.Abs(delta / 120));
        for (int step = 0; step < steps; step++)
            factor = delta > 0
                ? ZoomFactors.Skip(1).Where(z => z > factor + .000001).DefaultIfEmpty(factor).Min()
                : ZoomFactors.Skip(1).Where(z => z < factor - .000001).DefaultIfEmpty(factor).Max();
        int index = Array.IndexOf(ZoomFactors, factor);
        if (index < 1 || PreviewZoom == index) return true;
        PreviewZoom = index;
        int revision = ++_zoomRequestRevision;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (revision != _zoomRequestRevision || !ReferenceEquals(image, preview.Image)) return;
            preview.PlaceImagePointAt(anchor, pointer);
            var position = preview.ScrollPosition;
            if (!comparison)
            {
                var other = ReferenceEquals(preview, sourcePreview) ? resultPreview : sourcePreview;
                other.ScrollToRelativePosition(position.X, position.Y);
            }
        }));
        return true;
    }

    private int CompactableCellSize => _activeSource?.SelectedGeneration is { } generation
        ? generation.IsGlobalAlignment ? generation.CellSize : 0
        : _activeSource?.AlignedCellSize ?? 0;

    private void UpdateCompactButton()
    {
        int cellSize = CompactableCellSize;
        var image = _activeSource?.SelectedGeneration is null ? _sourceImage : _downscaledResult;
        compactGridButton.IsEnabled = !_processing && image is not null && cellSize >= 2;
        compactGridButton.ToolTip = _activeSource?.SelectedGeneration is { IsAlignment: true, IsGlobalAlignment: false }
            ? Localization.Text("Для сжатия до одного пикселя нужна общая глобальная сетка.")
            : image is not null && cellSize >= 2
            ? Localization.Format("Одна ячейка {0} × {1} → один пиксель. Размер: {2} × {3}. Цвета и прозрачность сохраняются.", cellSize, cellSize, (image.Width - 1) / cellSize + 1, (image.Height - 1) / cellSize + 1)
            : Localization.Text("Выберите выровненный результат или добавленный из него исходник.");
    }

    private async void CompactGrid(object? sender, RoutedEventArgs e)
    {
        if (_processing || _activeSource is null || CompactableCellSize < 2) return;
        var document = _activeSource;
        var parent = document.SelectedGeneration;
        int cellSize = CompactableCellSize;
        var input = parent is null ? _sourceImage : _downscaledResult;
        if (input is null) return;
        var settings = parent?.Settings ?? CaptureSettings();
        SetProcessingState(true);
        statusLabel.Text = Localization.Format("Сжатие сетки: ячейка {0} × {1} → один пиксель…", cellSize, cellSize);
        try
        {
            using var source = (Bitmap)input.Clone();
            var generation = await Task.Run(() =>
            {
                using var compact = PixelGridReducer.Reduce(source, cellSize);
                return _library.SaveGeneration(document, compact, null, new GenerationEntry
                {
                    OperationId = ProcessingOperations.Reduce,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = ProcessingOperations.Reduce, CellSize = cellSize, UsePreviousAlignment = parent?.IsAlignment == true && parent.Settings.DetectGrid }),
                    Settings = settings, IsGridReduction = true, PreservesTransparency = true,
                    ParentGenerationId = parent?.Id, CellSize = cellSize,
                    Caption = Localization.Format("СЕТКА В 1 px · {0} × {1} · из ячейки {2} px", compact.Width, compact.Height, cellSize),
                    Log = Localization.Format("{0} × {1} → {2} × {3}. Одна ячейка — один пиксель. Неполные крайние ячейки сохранены.", source.Width, source.Height, compact.Width, compact.Height)
                });
            });
            document.Generations.Add(generation);
            SelectGeneration(generation);
            statusLabel.Text = Localization.Format("Готово · {0} × {1}. Каждая ячейка стала одним пикселем. Результат сохранён в истории.", generation.Width, generation.Height);
        }
        catch (Exception ex)
        {
            statusLabel.Text = Localization.Format("Не удалось сжать сетку: {0}", ex.Message);
        }
        finally { SetProcessingState(false); }
    }

    private DataObject CreateGenerationDragData(SourceEntry source, GenerationEntry generation)
        => new(GenerationDragFormat, $"{source.Id:N}:{generation.Id:N}");

    private bool TryGetDraggedGeneration(IDataObject data, out SourceEntry source, out GenerationEntry generation)
    {
        source = null!; generation = null!;
        if (!data.GetDataPresent(GenerationDragFormat) || data.GetData(GenerationDragFormat) is not string text) return false;
        var parts = text.Split(':');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var sourceId) || !Guid.TryParseExact(parts[1], "N", out var generationId)) return false;
        source = _sources.FirstOrDefault(s => s.Id == sourceId)!;
        generation = source?.Generations.FirstOrDefault(g => g.Id == generationId)!;
        return source is not null && generation is not null;
    }

    private bool IsSourceDropTarget(DependencyObject? target)
    {
        for (var current = target; current is not null; current = ParentOf(current))
            if (ReferenceEquals(current, sourcePane) || ReferenceEquals(current, sourceTray)) return true;
        return false;
    }

    private static T? Within<T>(DependencyObject? target) where T : DependencyObject
    {
        for (var current = target; current is not null; current = ParentOf(current)) if (current is T match) return match;
        return null;
    }
    private static DependencyObject? ParentOf(DependencyObject target) => target is Visual or System.Windows.Media.Media3D.Visual3D
        ? VisualTreeHelper.GetParent(target) : LogicalTreeHelper.GetParent(target);

    private async Task AddGenerationAsSourceAsync(SourceEntry source, GenerationEntry generation)
    {
        if (_processing) return;
        SetProcessingState(true);
        try
        {
            var existing = _sources.ToArray();
            var imported = await Task.Run(() => _library.ImportGeneration(source, generation, existing));
            if (!_sources.Contains(imported)) _sources.Add(imported);
            if (_workspaceReady && _workspaceCatalog!.State.Assets.FirstOrDefault(a => a.Id == imported.Id) is { } asset) AddNewWorkspaceAsset(asset);
            SelectSource(imported);
            statusLabel.Text = Localization.Text("Результат добавлен как отдельный исходник. Можно продолжать обработку.");
        }
        catch (Exception ex) { statusLabel.Text = Localization.Format("Не удалось добавить результат: {0}", ex.Message); }
        finally { SetProcessingState(false); }
    }
}
