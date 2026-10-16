using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace DomainColorTest;

public partial class TileInspectorWindow
{
    private static readonly double[] TileZoomFactors = [0, .0625, .125, .25, .5, 1, 2, 4, 8, 16, 32, 64];
    private int _zoomRequestRevision;

    private bool ChangePreviewZoom(int delta, Point pointer, ModifierKeys modifiers)
    {
        if ((modifiers & ModifierKeys.Control) == 0 || preview.Source is null || delta == 0) return false;
        FinishStroke();
        var image = preview.Source;
        previewScroll.UpdateLayout();
        var position = previewScroll.TranslatePoint(pointer, preview);
        var anchor = new Point(Math.Clamp(position.X / preview.PixelScale, 0, image.PixelWidth * preview.Columns),
            Math.Clamp(position.Y / preview.PixelScale, 0, image.PixelHeight * preview.Rows));
        double factor = preview.PixelScale;
        int steps = Math.Min(TileZoomFactors.Length, Math.Max(1, Math.Abs(delta / 120)));
        for (int q = 0; q < steps; q++)
            factor = delta > 0
                ? TileZoomFactors.Skip(1).Where(value => value > factor + .000001).DefaultIfEmpty(factor).Min()
                : TileZoomFactors.Skip(1).Where(value => value < factor - .000001).DefaultIfEmpty(factor).Max();
        int index = Array.IndexOf(TileZoomFactors, factor);
        if (index < 1 || zoomInput.SelectedIndex == index) return true;
        zoomInput.SelectedIndex = index;
        int revision = _zoomRequestRevision;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (_closed || revision != _zoomRequestRevision || !ReferenceEquals(image, preview.Source)) return;
            previewScroll.UpdateLayout();
            var target = preview.TranslatePoint(new Point(anchor.X * preview.PixelScale, anchor.Y * preview.PixelScale), previewScroll);
            previewScroll.ScrollToHorizontalOffset(previewScroll.HorizontalOffset + target.X - pointer.X);
            previewScroll.ScrollToVerticalOffset(previewScroll.VerticalOffset + target.Y - pointer.Y);
        }));
        return true;
    }
}
