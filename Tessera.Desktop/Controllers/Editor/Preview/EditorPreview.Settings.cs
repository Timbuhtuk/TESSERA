using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tessera.Desktop;

internal sealed partial class EditorPreview
{
    private int _previewZoom, _previewBackground;

    public int PreviewZoom
    {
        get => _previewZoom;
        set { _previewZoom = Math.Clamp(value, 0, ZoomFactors.Length - 1); UpdatePreviewSettings(); Host.UpdateNavigationChecks(); }
    }

    public int PreviewBackground
    {
        get => _previewBackground;
        set { _previewBackground = Math.Clamp(value, 0, 3); UpdatePreviewSettings(); Host.UpdateNavigationChecks(); }
    }
}
