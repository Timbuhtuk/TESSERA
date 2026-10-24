using System.Windows;
using System.Windows.Threading;

namespace DomainColorTest;

public partial class MaterialMapsWindow
{
    private MaterialMapView[] AllViews => [heightView, normalView, aoView, litView];
    private Point _viewCenter;
    private bool _viewportQueued, _syncingViewport;

    private void QueueViewport()
    {
        if (!_ready || _closed || _viewportQueued) return;
        _viewportQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _viewportQueued = false;
            if (_closed || Document is null) return;
            _syncingViewport = true;
            try
            {
                double fit = AllViews.Min(v => v.FitScale);
                double zoom = zoomInput.SelectedIndex <= 0 ? fit >= 1 ? Math.Max(1, Math.Floor(fit)) : fit : Math.Pow(2, zoomInput.SelectedIndex - 1);
                if (zoomInput.SelectedIndex <= 0) _viewCenter = new Point(Document.Width / 2.0, Document.Height / 2.0);
                foreach (var view in AllViews) view.Zoom = zoom;
                UpdateLayout();
                foreach (var view in AllViews) view.CenterOn(_viewCenter);
                UpdateLayout();
            }
            finally { _syncingViewport = false; }
        }));
    }

    private void SynchronizeScroll(MaterialMapView source)
    {
        if (!_ready || _syncingViewport) return;
        // Wheel scrolling updates ScrollInfo during layout. Read its final offsets after that pass.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => ApplyScroll(source)));
    }

    private void ApplyScroll(MaterialMapView source)
    {
        if (_closed || !_ready || _syncingViewport) return;
        var center = source.ViewCenter;
        if ((center - _viewCenter).Length < .01) return;
        _viewCenter = center;
        _syncingViewport = true;
        try
        {
            foreach (var view in AllViews.Where(v => v != source)) view.CenterOn(center);
            UpdateLayout();
        }
        finally { _syncingViewport = false; }
    }
}
