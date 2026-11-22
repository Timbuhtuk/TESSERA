using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Tessera.Desktop;

internal sealed partial class EditorPreview
{
    internal void ResizeForWindow(double width)
    {
        bool compact = width < 940;
        double editorInset = compact ? 12 : 18;
        _editor.previewArea.Margin = new Thickness(0, 0, 0, compact ? 10 : 12);
        _editor.historyRow.Margin = _editor.sourceTray.Margin = new Thickness(editorInset, 0, editorInset, 0);
        _editor.sourceLibraryToggle.Margin = new Thickness(editorInset, 8, editorInset, 0);
        _editor.workspaceHost.VerticalScrollBarVisibility = compact ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        UpdatePreviewOrientation();
        UpdatePreviewHeight();
    }

    private void UpdatePreviewOrientation()
    {
        bool wide = Host.ActualWidth >= 1200;
        if ((Grid.GetColumn(_editor.sourcePane) == 2) == wide) return;

        _editor.Atlas.ResizePreview(wide);
        _editor.previewPair.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
        _editor.previewPair.RowDefinitions[1].Height = new GridLength(wide ? 0 : 6);
        _editor.previewPair.RowDefinitions[2].Height = wide ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        _editor.previewPair.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        _editor.previewPair.ColumnDefinitions[1].Width = new GridLength(wide ? 6 : 0);
        _editor.previewPair.ColumnDefinitions[2].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetRow(_editor.sourcePane, wide ? 0 : 2);
        Grid.SetColumn(_editor.sourcePane, wide ? 2 : 0);
        Grid.SetRow(_editor.previewSplitter, wide ? 0 : 1);
        Grid.SetColumn(_editor.previewSplitter, wide ? 1 : 0);
        _editor.previewSplitter.ResizeDirection = wide ? GridResizeDirection.Columns : GridResizeDirection.Rows;
        _editor.previewSplitter.Width = wide ? 6 : double.NaN;
        _editor.previewSplitter.Height = wide ? double.NaN : 6;
        _editor.previewSplitter.HorizontalAlignment = wide ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        _editor.previewSplitter.VerticalAlignment = wide ? VerticalAlignment.Stretch : VerticalAlignment.Center;
    }

    internal void UpdatePreviewHeight()
    {
        double minimum = 366;
        _editor.workspaceGrid.MinHeight = Math.Max(minimum, _editor.workspaceHost.ActualHeight);
    }
}
