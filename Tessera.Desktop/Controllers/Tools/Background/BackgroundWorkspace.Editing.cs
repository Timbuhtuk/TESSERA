using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace Tessera.Desktop;

public partial class BackgroundWorkspace
{
    private Color _customBackgroundColor = Color.White;
    private Bitmap? _processedResult;
    private bool[]? _manualEraseMask;
    private readonly Stack<int[]> _eraseUndo = new();
    private HashSet<int>? _eraseStroke;
    private Point? _lastErasePoint;
    private int _erasedCount;
    private bool _erasePreviewDirty;
    private readonly DispatcherTimer _erasePreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(24) };

    private void InitializeEditing()
    {
        backgroundCustomColorOption.Content = Localization.Text("CustomColor");
        backgroundChooseColorButton.ToolTip = Localization.Text("OpenColorPickerChooseBackgroundColor");
        backgroundPickColorToggle.Content = Localization.Text("PickFromSource");
        backgroundPickColorToggle.ToolTip = Localization.Text("EnableClickVisibleSourcePixelChoose");
        backgroundManualTitle.Text = Localization.Text("ManualEdgeCleanup");
        backgroundEraserToggle.Content = Localization.Text("EraseOnResult");
        backgroundEraserToggle.ToolTip = Localization.Text("HoldLeftMouseButtonDragResult");
        backgroundBrushSizeTitle.Text = Localization.Text("BrushSize");
        backgroundBrushSizeInput.ToolTip = Localization.Text("RoundEraserDiameterInImagePixels");
        backgroundBrushSizeInput.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, Localization.Text("EraserBrushSize"));
        backgroundUndoEraseButton.Content = Localization.Text("UndoStroke");
        backgroundUndoEraseButton.ToolTip = Localization.Text("RestorePixelsLastEraserStroke");
        backgroundResetEraseButton.Content = Localization.Text("ResetEdits");
        backgroundResetEraseButton.ToolTip = Localization.Text("RemoveAllManualStrokesWhileKeeping");
        backgroundManualHint.Text = Localization.Text("EraserStrokesStayWhenYouChange");
        backgroundZoomTitle.Text = Localization.Text("PreviewZoomLabel");
        backgroundFitZoomOption.Content = Localization.Text("Fit");
        backgroundZoomInput.ToolTip = Localization.Text("ZoomInForPreciseEdgeCleanup");
        backgroundColorInput.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, Localization.Text("BackgroundColorToRemove"));
        backgroundZoomInput.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, Localization.Text("PreviewZoomLabel"));

        backgroundChooseColorButton.Click += (_, _) => ChooseBackgroundColor();
        backgroundPickColorToggle.Checked += (_, _) =>
        {
            backgroundEraserToggle.IsChecked = false;
            backgroundSourcePreview.IsColorPicking = true;
        };
        backgroundPickColorToggle.Unchecked += (_, _) => backgroundSourcePreview.IsColorPicking = false;
        backgroundSourcePreview.ColorPicked += (_, e) =>
        {
            SetCustomBackgroundColor(e.Color);
            backgroundPickColorToggle.IsChecked = false;
        };
        backgroundEraserToggle.Checked += (_, _) =>
        {
            backgroundPickColorToggle.IsChecked = false;
            UpdateEditingState();
        };
        backgroundEraserToggle.Unchecked += (_, _) => { FinishEraseStroke(); UpdateEditingState(); };
        backgroundUndoEraseButton.Click += (_, _) => UndoEraseStroke();
        backgroundResetEraseButton.Click += (_, _) =>
        {
            FinishEraseStroke();
            ResetManualEdits();
            RecomposeManualResult();
            UpdateEditingState();
        };
        backgroundZoomInput.SelectionChanged += (_, _) =>
        {
            FinishEraseStroke();
            double zoom = backgroundZoomInput.SelectedIndex <= 0 ? 0 : Math.Pow(2, backgroundZoomInput.SelectedIndex - 1);
            backgroundSourcePreview.Zoom = backgroundResultPreview.Zoom = zoom;
        };
        foreach (var preview in new[] { backgroundSourcePreview, backgroundResultPreview })
            preview.PreviewMouseWheel += (_, e) =>
            {
                if (!ImageViewportInput.IsZoomGesture(Keyboard.Modifiers) || e.Delta == 0 || preview.Image is null) return;
                backgroundZoomInput.SelectedIndex = Math.Clamp(backgroundZoomInput.SelectedIndex + Math.Sign(e.Delta), 0, backgroundZoomInput.Items.Count - 1);
                e.Handled = true;
            };

        backgroundResultPreview.Focusable = true;
        backgroundResultPreview.PreviewMouseLeftButtonDown += EraserMouseDown;
        backgroundResultPreview.PreviewMouseMove += EraserMouseMove;
        backgroundResultPreview.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_eraseStroke is null) return;
            EraseAtMouse(e);
            FinishEraseStroke();
            e.Handled = true;
        };
        backgroundResultPreview.LostMouseCapture += (_, _) => FinishEraseStroke();
        _erasePreviewTimer.Tick += (_, _) => RefreshErasePreview();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                backgroundPickColorToggle.IsChecked = backgroundEraserToggle.IsChecked = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && _eraseUndo.Count > 0 && !IsRendering && !_busy)
            {
                UndoEraseStroke();
                e.Handled = true;
            }
        };
        Unloaded += (_, _) =>
        {
            FinishEraseStroke();
            backgroundPickColorToggle.IsChecked = backgroundEraserToggle.IsChecked = false;
        };
        UpdateBackgroundColorDisplay();
        UpdateEditingState();
    }

    private void ChooseBackgroundColor()
    {
        backgroundPickColorToggle.IsChecked = false;
        var picker = new ColorReplaceWindow(GetOptions().Color ?? _customBackgroundColor) { Owner = Window.GetWindow(this) };
        picker.ShowDialog();
        if (picker.SelectedColor is Color color) SetCustomBackgroundColor(color);
    }

    public void SetCustomBackgroundColor(Color color)
    {
        _customBackgroundColor = Color.FromArgb(color.R, color.G, color.B);
        bool alreadyCustom = backgroundColorInput.SelectedIndex == 3;
        backgroundColorInput.SelectedIndex = 3;
        if (alreadyCustom) SchedulePreview();
    }

    private void UpdateBackgroundColorDisplay()
    {
        Color? selected = GetOptions().Color;
        backgroundSelectedSwatch.Background = selected is Color color
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B)) : Brushes.Transparent;
        backgroundSelectedColorText.Text = selected is Color chosen
            ? PaletteColorEntry.FormatHex(chosen) + " · " + Localization.Text("ChangeAction")
            : Localization.Text("ChooseCustomColorAction");
    }

    private void UpdateEditingState()
    {
        bool ready = !_busy && !_disposed && !IsRendering && _result is not null;
        backgroundPickColorToggle.IsEnabled = !_busy && !_disposed && _source is not null;
        backgroundEraserToggle.IsEnabled = ready;
        backgroundBrushSizeInput.IsEnabled = ready;
        backgroundUndoEraseButton.IsEnabled = ready && (_eraseUndo.Count > 0 || _eraseStroke?.Count > 0);
        backgroundResetEraseButton.IsEnabled = ready && _erasedCount > 0;
        backgroundResultPreview.Cursor = ready && backgroundEraserToggle.IsChecked == true ? Cursors.Cross : null;
    }

    private void EraserMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (backgroundEraserToggle.IsChecked != true || _result is null || IsRendering || _busy) return;
        for (var current = e.OriginalSource as DependencyObject; current is not null && !ReferenceEquals(current, backgroundResultPreview); current = VisualTreeHelper.GetParent(current))
            if (current is ScrollBar) return;
        if (!backgroundResultPreview.TryGetImagePixel(e.GetPosition(backgroundResultPreview), out _, out _)) return;
        backgroundResultPreview.Focus();
        _eraseStroke = new HashSet<int>();
        _lastErasePoint = null;
        backgroundResultPreview.CaptureMouse();
        EraseAtMouse(e);
        e.Handled = true;
    }

    private void EraserMouseMove(object sender, MouseEventArgs e)
    {
        if (_eraseStroke is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { FinishEraseStroke(); return; }
        EraseAtMouse(e);
        e.Handled = true;
    }

    private void EraseAtMouse(MouseEventArgs e)
    {
        if (_eraseStroke is null || _result is null) return;
        if (!backgroundResultPreview.TryGetImagePixel(e.GetPosition(backgroundResultPreview), out int x, out int y))
        {
            // Do not draw a chord across the image when the pointer leaves and re-enters it.
            _lastErasePoint = null;
            return;
        }
        var point = new Point(x, y);
        PaintEraseSegment(_lastErasePoint ?? point, point);
        _lastErasePoint = point;
    }

    private void PaintEraseSegment(Point from, Point to)
    {
        if (_result is null || _eraseStroke is null) return;
        int width = _result.Width, height = _result.Height;
        _manualEraseMask ??= new bool[checked(width * height)];
        double radius = backgroundBrushSizeInput.Value / 2;
        int left = Math.Max(0, (int)Math.Floor(Math.Min(from.X, to.X) - radius));
        int top = Math.Max(0, (int)Math.Floor(Math.Min(from.Y, to.Y) - radius));
        int right = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(from.X, to.X) + radius));
        int bottom = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(from.Y, to.Y) + radius));
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double lengthSquared = dx * dx + dy * dy;
        var bounds = new System.Drawing.Rectangle(0, 0, width, height);
        var bits = _result.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        bool changed = false;
        try
        {
            for (int y = top; y <= bottom; y++)
            {
                for (int x = left; x <= right; x++)
                {
                    int q = y * width + x;
                    if (_manualEraseMask[q]) continue;
                    double projection = lengthSquared == 0 ? 0 : Math.Clamp(((x - from.X) * dx + (y - from.Y) * dy) / lengthSquared, 0, 1);
                    double distanceX = x - from.X - projection * dx, distanceY = y - from.Y - projection * dy;
                    if (distanceX * distanceX + distanceY * distanceY > radius * radius) continue;
                    _manualEraseMask[q] = true;
                    _eraseStroke.Add(q);
                    _erasedCount++;
                    Marshal.WriteInt32(IntPtr.Add(bits.Scan0, y * bits.Stride + x * 4), 0);
                    changed = true;
                }
            }
        }
        finally { _result.UnlockBits(bits); }
        if (!changed) return;
        _erasePreviewDirty = true;
        _erasePreviewTimer.Start();
        UpdateEditingState();
    }

    private void FinishEraseStroke()
    {
        var stroke = _eraseStroke;
        _eraseStroke = null;
        _lastErasePoint = null;
        if (stroke is { Count: > 0 }) _eraseUndo.Push(stroke.ToArray());
        if (backgroundResultPreview.IsMouseCaptured) backgroundResultPreview.ReleaseMouseCapture();
        RefreshErasePreview();
        UpdateEditingState();
    }

    private void RefreshErasePreview()
    {
        _erasePreviewTimer.Stop();
        if (!_erasePreviewDirty) return;
        _erasePreviewDirty = false;
        if (!_disposed && _result is not null) backgroundResultPreview.Image = _result;
    }

    private void UndoEraseStroke()
    {
        if (IsRendering || _busy) return;
        FinishEraseStroke();
        if (_manualEraseMask is null || !_eraseUndo.TryPop(out var stroke)) return;
        foreach (int q in stroke) _manualEraseMask[q] = false;
        _erasedCount -= stroke.Length;
        RecomposeManualResult();
        UpdateEditingState();
    }

    private void ResetManualEdits()
    {
        _erasePreviewTimer.Stop();
        _erasePreviewDirty = false;
        _manualEraseMask = null;
        _eraseUndo.Clear();
        _erasedCount = 0;
    }

    private void RecomposeManualResult()
    {
        if (_processedResult is null) return;
        var composed = (Bitmap)_processedResult.Clone();
        try
        {
            if (_manualEraseMask is not null && _erasedCount > 0)
            {
                int width = composed.Width, height = composed.Height;
                var bits = composed.LockBits(new System.Drawing.Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                            if (_manualEraseMask[y * width + x]) Marshal.WriteInt32(IntPtr.Add(bits.Scan0, y * bits.Stride + x * 4), 0);
                }
                finally { composed.UnlockBits(bits); }
            }
            backgroundResultPreview.Image = composed;
        }
        catch { composed.Dispose(); throw; }
        _result?.Dispose();
        _result = composed;
        _erasePreviewDirty = false;
    }
}
