using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using Bitmap = System.Drawing.Bitmap;
using Tessera.Core.Imaging;
using Tessera.Core.Alignment;

namespace Tessera.Desktop;

internal sealed partial class EditorPreview
{
    internal void UpdatePreviewSettings()
    {
        double zoom = ZoomFactors[Math.Clamp(PreviewZoom, 0, ZoomFactors.Length - 1)];
        foreach (var preview in new[] { _editor.sourcePreview, _editor.resultPreview, _editor.Atlas.Preview })
        {
            preview.Zoom = zoom;
            preview.PreviewBackground = PreviewBackground;
        }
        UpdateComparisonPreview();
        _editor.Atlas.UpdateAtlasRepeat();
    }

    internal void UpdateSourcePreview()
    {
        _editor.sourcePreview.Image = _editor._sourceImage;
        if (Host._workspaceCatalog?.State.Projects.FirstOrDefault(p => p.Id == Host._workspaceSourceProject) is { } project)
            _editor.History.BuildSourceBreadcrumb(project);
        else _editor.sourceCaption.Text = _editor._sourceImage is null ? Localization.Text("Source") :
            Localization.Format("SourceFormat", _editor._sourceImage.Width, _editor._sourceImage.Height);
        UpdateComparisonPreview();
    }
}
