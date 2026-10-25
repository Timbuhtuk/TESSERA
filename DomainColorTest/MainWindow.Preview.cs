using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace DomainColorTest;

public partial class MainWindow
{
    private readonly PixelPreview _comparisonPreview = new() { ContinuousFit = true, Focusable = true, Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer _comparisonKeyTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly HashSet<Key> _comparisonAltKeys = [];
    private MenuItem _alternativePreviewItem = null!;
    private bool _alternativePreview, _comparisonOriginal, _comparisonAltOwned;

    public bool AlternativePreview
    {
        get => _alternativePreview;
        set
        {
            _alternativePreview = value;
            ResetComparisonKey();
            UpdateComparisonPreview();
            UpdateNavigationChecks();
            if (value) _comparisonPreview.Focus();
        }
    }

    private void InitializePreviewMode()
    {
        _comparisonPreview.Name = "comparisonPreview";
        RegisterName(_comparisonPreview.Name, _comparisonPreview);
        _comparisonPreview.EmptyText = Localization.Text("Выберите результат для сравнения. Удерживайте Alt, чтобы показать исходник.");
        _comparisonPreview.ToolTip = Localization.Text("Результат занимает всё поле. Удерживайте Alt для исходника; отпустите для результата. Колесо меняет масштаб, Ctrl + колесо прокручивает изображение.");
        AutomationProperties.SetName(_comparisonPreview, Localization.Text("Сравнение исходника и результата"));
        Grid.SetRow(_comparisonPreview, 2);
        previewArea.Children.Add(_comparisonPreview);
        _comparisonPreview.MouseLeftButtonDown += (_, _) => _comparisonPreview.Focus();
        PreviewKeyDown += ComparisonKeyDown;
        PreviewKeyUp += ComparisonKeyUp;
        Deactivated += (_, _) => ResetComparisonKey();
        LostKeyboardFocus += (_, _) =>
        {
            if (Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox)
                ResetComparisonKey();
        };
        workspaceHost.IsVisibleChanged += (_, _) => { ResetComparisonKey(); UpdateComparisonPreview(); };
        _comparisonKeyTimer.Tick += (_, _) =>
        {
            if (!IsActive || !IsVisible || !Keyboard.IsKeyDown(Key.LeftAlt) && !Keyboard.IsKeyDown(Key.RightAlt))
                ResetComparisonKey();
        };
        Closed += (_, _) => { _comparisonKeyTimer.Stop(); _comparisonPreview.Image = null; };
    }

    private void ComparisonKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is not (Key.LeftAlt or Key.RightAlt))
        {
            _comparisonAltOwned = false;
            if (_comparisonOriginal) ResetComparisonKey();
            return;
        }
        if (e.IsRepeat || !_alternativePreview || !ImageEditorVisible || _downscaledResult is null ||
            (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows)) != 0 ||
            Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox || mainMenu.IsKeyboardFocusWithin) return;
        _comparisonOriginal = true;
        _comparisonAltKeys.Add(key);
        _comparisonAltOwned = true;
        _comparisonKeyTimer.Start();
        UpdateComparisonPreview();
        // Only a bare comparison Alt belongs to this mode. The next chord key remains unhandled.
        e.Handled = true;
    }

    private void ComparisonKeyUp(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is not (Key.LeftAlt or Key.RightAlt)) return;
        if (_comparisonAltOwned) e.Handled = true;
        _comparisonAltKeys.Remove(key);
        if (_comparisonAltKeys.Count > 0) return;
        ResetComparisonKey();
    }

    private void ResetComparisonKey()
    {
        _comparisonKeyTimer.Stop();
        _comparisonAltOwned = false;
        _comparisonAltKeys.Clear();
        if (!_comparisonOriginal) return;
        _comparisonOriginal = false;
        UpdateComparisonPreview();
    }

    private void UpdateComparisonPreview()
    {
        bool enabled = _alternativePreview;
        previewPair.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        _comparisonPreview.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        _comparisonPreview.PreviewBackground = PreviewBackground;
        _comparisonPreview.Zoom = ZoomFactors[Math.Clamp(PreviewZoom, 0, ZoomFactors.Length - 1)];
        var image = enabled ? _comparisonOriginal ? _sourceImage : _downscaledResult : null;
        if (!ReferenceEquals(_comparisonPreview.Image, image)) _comparisonPreview.Image = image;
        UpdateCropPreview();
    }

    private void UpdateSharedPreviewScale()
    {
        var source = sourcePreview.Image;
        var result = resultPreview.Image;
        System.Drawing.Size? reference = source is null || result is null ? null :
            (long)source.Width * source.Height >= (long)result.Width * result.Height ? source.Size : result.Size;
        sourcePreview.ReferenceImageSize = reference;
        resultPreview.ReferenceImageSize = reference;
    }

    private void UpdateCropPreview()
    {
        sourcePreview.SelectedRegion = resultPreview.SelectedRegion = _comparisonPreview.SelectedRegion = null;
        var basis = ProcessingBasis == 1 ? _downscaledResult : _sourceImage;
        if (basis is null || spriteInput.IsChecked == true) return;
        int width = (int)widthInput.Value, height = (int)heightInput.Value;
        if (width < 1 || height < 1 || width > basis.Width || height > basis.Height) return;
        int cropWidth = basis.Width / width * width;
        int cropHeight = basis.Height / height * height;
        if (cropWidth == basis.Width && cropHeight == basis.Height) return;
        int x = cropHorizontalInput.SelectedIndex switch { 1 => 0, 2 => basis.Width - cropWidth, _ => (basis.Width - cropWidth) / 2 };
        int y = cropVerticalInput.SelectedIndex switch { 1 => 0, 2 => basis.Height - cropHeight, _ => (basis.Height - cropHeight) / 2 };
        var region = new Rect(x, y, cropWidth, cropHeight);
        (ProcessingBasis == 1 ? resultPreview : sourcePreview).SelectedRegion = region;
        if (ReferenceEquals(_comparisonPreview.Image, basis)) _comparisonPreview.SelectedRegion = region;
    }
}
