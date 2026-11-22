using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

internal sealed partial class EditorTools
{
    internal void InitializeSizeFactor()
    {
        _editor.pixelSizeModeInput.Content = Localization.Text("SizePx");
        _editor.scaleFactorModeInput.Content = Localization.Text("Scale");
        _editor.scaleFactorLabel.Text = Localization.Text("Factor");
        _editor.scaleFactorInput.ToolTip = Localization.Text("Example05HalvesBothDimensions");
        AutomationProperties.SetName(_editor.scaleFactorInput, Localization.Text("ScaleFactor"));
        _editor.pixelSizeModeInput.Checked += (_, _) => UpdateSizeInputMode();
        _editor.scaleFactorModeInput.Checked += (_, _) => UpdateSizeInputMode();
        _editor.scaleFactorInput.TextChanged += (_, _) =>
        {
            if (_editor.History.SelectionUpdatesPaused) return;
            MarkResultStale();
            UpdateSizeHint();
        };
    }

    private void UpdateSizeInputMode()
    {
        bool factorMode = _editor.scaleFactorModeInput.IsChecked == true;
        _editor.pixelSizeControls.Visibility = factorMode ? Visibility.Collapsed : Visibility.Visible;
        _editor.scaleFactorControls.Visibility = factorMode ? Visibility.Visible : Visibility.Collapsed;
        _editor.sizeHint.Visibility = factorMode ? Visibility.Visible : Visibility.Collapsed;
        if (_editor.History.SelectionUpdatesPaused) return;
        MarkResultStale();
        UpdateSizeHint();
    }

    internal bool TryGetTargetSize(out int width, out int height)
    {
        width = (int)_editor.widthInput.Value;
        height = (int)_editor.heightInput.Value;
        if (_editor.scaleFactorModeInput.IsChecked != true) return true;

        Bitmap? basis = _editor.ProcessingBasis == 1 ? _editor._downscaledResult : _editor._sourceImage;
        if (basis is null || !TryGetScaleFactor(out double factor)) return false;

        double scaledWidth = Math.Round(basis.Width * factor, MidpointRounding.AwayFromZero);
        double scaledHeight = Math.Round(basis.Height * factor, MidpointRounding.AwayFromZero);
        if (scaledWidth > int.MaxValue || scaledHeight > int.MaxValue) return false;
        width = Math.Max(1, (int)scaledWidth);
        height = Math.Max(1, (int)scaledHeight);
        return true;
    }

    private bool TryGetScaleFactor(out double factor) =>
        double.TryParse(_editor.scaleFactorInput.Text.Trim().Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out factor) && double.IsFinite(factor) && factor > 0;
}
