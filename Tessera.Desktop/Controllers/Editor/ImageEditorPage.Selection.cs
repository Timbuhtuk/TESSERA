using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tessera.Desktop;

public partial class ImageEditorPage
{
    private int _processingBasis;

    public int ProcessingBasis
    {
        get => _processingBasis;
        set
        {
            int next = value == 1 && _downscaledResult is not null ? 1 : 0;
            if (_processingBasis == next) return;
            _processingBasis = next;
            SyncProcessingSelection();
            if (aspectLock.IsChecked == true) Tools.SizeChangedByUser(widthChanged: true); else Tools.UpdateSizeHint();
            SetProcessingState(_processing);
            Preview.ScheduleLiveToolPreviews();
        }
    }

    internal bool ImageEditorVisible => workspaceHost.Visibility == Visibility.Visible && _sourceImage is not null && _activeSource is not null && (!Host._workspaceReady || Host._workspaceActive?.Kind == EWorkspaceAssetKind.Image);
}
