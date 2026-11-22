using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Tessera.Desktop;

internal sealed partial class EditorPreview
{
    private readonly ImageEditorPage _editor;
    private MainWindow Host => _editor.Host;

    internal EditorPreview(ImageEditorPage editor)
    {
        _editor = editor;
    }

    private readonly PixelPreview _comparisonPreview = new() { ContinuousFit = true, Focusable = true, Visibility = Visibility.Collapsed };

    private readonly DispatcherTimer _comparisonKeyTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };

    private bool _alternativePreview, _comparisonOriginal;

    private Window? _comparisonKeyWindow;

    private bool _cropToolActive;

    internal PixelPreview ComparisonSurface => _comparisonPreview;

    internal bool CropToolActive
    {
        get => _cropToolActive;
        set => _cropToolActive = value;
    }

    internal void ClearSourceImages()
    {
        _editor.sourcePreview.Image = null;
        _comparisonPreview.Image = null;
    }

    internal void ShowAtlas(bool visible)
    {
        MainWindow.SetVisible(_editor.previewPair, !visible && !AlternativePreview);
        MainWindow.SetVisible(_comparisonPreview, !visible && AlternativePreview);
    }

    public bool AlternativePreview
    {
        get => _alternativePreview;
        set
        {
            if (value)
            {
                _editor.Dock.CloseDockedTool(TileAtlasPage.ToolKey);
                foreach (var lens in AppearanceSettings.Lenses) CloseLens(lens.Tool);
            }
            _alternativePreview = value;
            ResetComparisonKey();
            UpdateComparisonPreview();
            Host.UpdateNavigationChecks();
            if (value) _comparisonPreview.Focus();
        }
    }

    internal void InitializePreviewMode()
    {
        _comparisonPreview.Name = "comparisonPreview";
        _editor.RegisterName(_comparisonPreview.Name, _comparisonPreview);
        UpdateComparisonKeyHints();
        AutomationProperties.SetName(_comparisonPreview, Localization.Text("SourceAndResultComparison"));
        Grid.SetRow(_comparisonPreview, 2);
        _editor.previewArea.Children.Add(_comparisonPreview);
        _comparisonPreview.MouseLeftButtonDown += (_, _) => _comparisonPreview.Focus();
        BindComparisonKey(Host);
        Host.PreviewKeyDown += (_, e) =>
        {
            if (_editor.Atlas.SelectPaintTool(e.Key, Keyboard.Modifiers, Keyboard.FocusedElement)) e.Handled = true;
        };
        _editor.workspaceHost.IsVisibleChanged += (_, _) => { ResetComparisonKey(); UpdateComparisonPreview(); };
        _comparisonKeyTimer.Tick += (_, _) =>
        {
            if (_comparisonKeyWindow is not { IsActive: true, IsVisible: true } ||
                !_editor.ImageEditorVisible || !Keyboard.IsKeyDown(Host._appearanceSettings.ComparisonPreviewKey))
                ResetComparisonKey();
        };
        Host.Closed += (_, _) => { _comparisonKeyTimer.Stop(); _comparisonPreview.Image = null; };
    }

    internal void BindComparisonKey(Window window)
    {
        window.PreviewKeyDown += ComparisonKeyDown;
        window.PreviewKeyUp += ComparisonKeyUp;
        window.Deactivated += (_, _) =>
        {
            if (_comparisonKeyWindow == window) ResetComparisonKey();
        };
        window.LostKeyboardFocus += (_, _) =>
        {
            if (_comparisonKeyWindow == window && !CanCompareWithKeyboard) ResetComparisonKey();
        };
    }

    private bool CanCompareWithKeyboard => !Host.mainMenu.IsKeyboardFocusWithin &&
        (Keyboard.FocusedElement is not (TextBoxBase or PasswordBox or ComboBox or ButtonBase or Slider) ||
         _editor.Atlas.HeatmapHasFocus);

    internal void UpdateComparisonKeyHints()
    {
        string key = AppearanceSettings.ComparisonKeyLabel(Host._appearanceSettings.ComparisonPreviewKey);
        string hint = Localization.Format("HoldToShowTheSourceFormat", key);
        _comparisonPreview.EmptyText = Localization.Text("SelectAResultToCompare") + " " + hint;
        _comparisonPreview.ToolTip = hint + " " + Localization.Text(ImageViewportInput.GestureHint);
        Host.SetComparisonHint(Localization.Text("ShowsSelectedResultAcrossPreviewArea") + " " + hint);
        _editor.Atlas.SetHeatmapKeyHint(key);
    }

    private void ComparisonKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Host._appearanceSettings.ComparisonPreviewKey) return;
        if (_comparisonKeyWindow is not null) { e.Handled = true; return; }
        if (e.IsRepeat || !_editor.ImageEditorVisible || Keyboard.Modifiers != ModifierKeys.None || !CanCompareWithKeyboard) return;
        if (_editor.Atlas.CanInvertHeatmap)
            _editor.Atlas.SetHeatmapInverted(true);
        else if (_alternativePreview && _editor._downscaledResult is not null)
        {
            _comparisonOriginal = true;
            UpdateComparisonPreview();
        }
        else if (!_alternativePreview && !_editor.Atlas.IsActive && !Host.WorkspaceBusy && ComparisonLens(key) is { } lens)
            SetComparisonLens(lens);
        else return;
        _comparisonKeyWindow = sender as Window ?? Host;
        _comparisonKeyTimer.Start();
        e.Handled = true;
    }

    private void ComparisonKeyUp(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Host._appearanceSettings.ComparisonPreviewKey || _comparisonKeyWindow is null) return;
        e.Handled = true;
        ResetComparisonKey();
    }

    internal void ResetComparisonKey()
    {
        _comparisonKeyTimer.Stop();
        _comparisonKeyWindow = null;
        SetComparisonLens(null);
        _editor.Atlas.SetHeatmapInverted(false);
        if (!_comparisonOriginal) return;
        _comparisonOriginal = false;
        UpdateComparisonPreview();
    }

    internal void UpdateComparisonPreview()
    {
        bool enabled = _alternativePreview;
        _editor.UpdateAtlasView();
        _comparisonPreview.PreviewBackground = PreviewBackground;
        _comparisonPreview.Zoom = ZoomFactors[Math.Clamp(PreviewZoom, 0, ZoomFactors.Length - 1)];
        _comparisonPreview.ReferenceImageSize = _editor._sourceImage is { } source && _editor._downscaledResult is { } result
            ? new System.Drawing.Size(Math.Max(source.Width, result.Width), Math.Max(source.Height, result.Height))
            : null;
        var image = enabled ? _comparisonOriginal ? _editor._sourceImage : _editor._downscaledResult : null;
        if (!ReferenceEquals(_comparisonPreview.Image, image)) _comparisonPreview.Image = image;
        UpdateCropPreview();
    }

    private void UpdateSharedPreviewScale()
    {
        var source = _editor.sourcePreview.Image;
        var result = _editor.resultPreview.Image;
        System.Drawing.Size? reference = source is null || result is null ? null :
            (long)source.Width * source.Height >= (long)result.Width * result.Height ? source.Size : result.Size;
        _editor.sourcePreview.ReferenceImageSize = reference;
        _editor.resultPreview.ReferenceImageSize = reference;
    }

    internal void UpdateCropPreview()
    {
        _editor.sourcePreview.SelectedRegion = _editor.resultPreview.SelectedRegion = _comparisonPreview.SelectedRegion = null;
        if (!_cropToolActive) return;
        var basis = _editor.ProcessingBasis == 1 ? _editor._downscaledResult : _editor._sourceImage;
        if (basis is null || _editor.spriteInput.IsChecked == true) return;
        if (!_editor.Tools.TryGetTargetSize(out int width, out int height)) return;
        if (width < 1 || height < 1 || width > basis.Width || height > basis.Height) return;
        int cropWidth = basis.Width / width * width;
        int cropHeight = basis.Height / height * height;
        if (cropWidth == basis.Width && cropHeight == basis.Height) return;
        int x = _editor.cropHorizontalInput.SelectedIndex switch { 1 => 0, 2 => basis.Width - cropWidth, _ => (basis.Width - cropWidth) / 2 };
        int y = _editor.cropVerticalInput.SelectedIndex switch { 1 => 0, 2 => basis.Height - cropHeight, _ => (basis.Height - cropHeight) / 2 };
        var region = new Rect(x, y, cropWidth, cropHeight);
        (_editor.ProcessingBasis == 1 ? _editor.resultPreview : _editor.sourcePreview).SelectedRegion = region;
        if (ReferenceEquals(_comparisonPreview.Image, basis)) _comparisonPreview.SelectedRegion = region;
    }
}
