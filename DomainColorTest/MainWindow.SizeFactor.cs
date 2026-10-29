using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class MainWindow
{
    private void InitializeSizeFactor()
    {
        pixelSizeModeInput.Content = Localization.Text("Размер, px");
        scaleFactorModeInput.Content = Localization.Text("Масштаб размера");
        scaleFactorLabel.Text = Localization.Text("Коэффициент");
        scaleFactorInput.ToolTip = Localization.Text("Например, 0.5 уменьшит обе стороны вдвое, а 1.5 увеличит в полтора раза.");
        AutomationProperties.SetName(scaleFactorInput, Localization.Text("Коэффициент масштаба"));
        pixelSizeModeInput.Checked += (_, _) => UpdateSizeInputMode();
        scaleFactorModeInput.Checked += (_, _) => UpdateSizeInputMode();
        scaleFactorInput.TextChanged += (_, _) =>
        {
            if (_restoringHistory) return;
            MarkResultStale();
            UpdateSizeHint();
        };
    }

    private void UpdateSizeInputMode()
    {
        bool factorMode = scaleFactorModeInput.IsChecked == true;
        pixelSizeControls.Visibility = factorMode ? Visibility.Collapsed : Visibility.Visible;
        scaleFactorControls.Visibility = factorMode ? Visibility.Visible : Visibility.Collapsed;
        sizeHint.Visibility = factorMode ? Visibility.Visible : Visibility.Collapsed;
        if (_restoringHistory) return;
        MarkResultStale();
        UpdateSizeHint();
    }

    private bool TryGetTargetSize(out int width, out int height)
    {
        width = (int)widthInput.Value;
        height = (int)heightInput.Value;
        if (scaleFactorModeInput.IsChecked != true) return true;

        Bitmap? basis = ProcessingBasis == 1 ? _downscaledResult : _sourceImage;
        if (basis is null || !double.TryParse(scaleFactorInput.Text.Trim().Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out double factor) ||
            !double.IsFinite(factor) || factor <= 0) return false;

        double scaledWidth = Math.Round(basis.Width * factor, MidpointRounding.AwayFromZero);
        double scaledHeight = Math.Round(basis.Height * factor, MidpointRounding.AwayFromZero);
        if (scaledWidth > int.MaxValue || scaledHeight > int.MaxValue) return false;
        width = Math.Max(1, (int)scaledWidth);
        height = Math.Max(1, (int)scaledHeight);
        return true;
    }
}
