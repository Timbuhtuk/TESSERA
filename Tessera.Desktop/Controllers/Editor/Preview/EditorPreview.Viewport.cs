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

internal sealed partial class EditorPreview
{
    internal static readonly double[] ZoomFactors = [0, .0625, .125, .25, .5, 1, 2, 4, 8, 16, 32, 64];

    private int _zoomRequestRevision;

    internal static string ResultPreviewHint => Localization.Text(ImageViewportInput.GestureHint) + " " +
        Localization.Text("DragResultIntoSourcesProcessIt");

    internal void InitializeViewport()
    {
        _editor.workspaceHost.SizeChanged += (_, _) => UpdatePreviewHeight();
        _editor.resultPreview.ToolTip = ResultPreviewHint;
        _editor.sourcePreview.ImageChanged += (_, _) => UpdateSharedPreviewScale();
        _editor.resultPreview.ImageChanged += (_, _) => UpdateSharedPreviewScale();
        foreach (var preview in new[] { _editor.sourcePreview, _editor.resultPreview, _comparisonPreview, _editor.Atlas.Preview })
            preview.PreviewMouseWheel += (_, e) =>
            {
                if (ChangePreviewZoom(preview, e.Delta, e.GetPosition(preview), Keyboard.Modifiers)) e.Handled = true;
            };
    }

    internal void InitializeScrollSync()
    {
        foreach (var preview in new[] { _editor.sourcePreview, _editor.resultPreview })
            preview.ScrollPositionChanged += (_, _) =>
            {
                var other = ReferenceEquals(preview, _editor.sourcePreview) ? _editor.resultPreview : _editor.sourcePreview;
                var position = preview.ScrollPosition;
                other.ScrollToRelativePosition(position.X, position.Y);
            };
    }

    private bool ChangePreviewZoom(PixelPreview preview, int delta, Point pointer, ModifierKeys modifiers)
    {
        bool paired = ReferenceEquals(preview, _editor.sourcePreview) || ReferenceEquals(preview, _editor.resultPreview);
        if (!ImageViewportInput.IsZoomGesture(modifiers) || preview.Image is null || delta == 0) return false;
        var image = preview.Image;
        var anchor = preview.ImagePointAt(pointer);
        double factor = PreviewZoom > 0 ? ZoomFactors[PreviewZoom] : preview.EffectiveZoom;
        if (paired && PreviewZoom == 0 && preview.ReferenceImageSize is { } reference)
        {
            var referencePreview = _editor.sourcePreview.Image?.Size == reference ? _editor.sourcePreview : _editor.resultPreview;
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
        _editor.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (revision != _zoomRequestRevision || !ReferenceEquals(image, preview.Image)) return;
            preview.PlaceImagePointAt(anchor, pointer);
            var position = preview.ScrollPosition;
            if (paired)
            {
                var other = ReferenceEquals(preview, _editor.sourcePreview) ? _editor.resultPreview : _editor.sourcePreview;
                other.ScrollToRelativePosition(position.X, position.Y);
            }
        }));
        return true;
    }
}
