using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DomainColorTest;

public sealed record EditorMaterial(string Label, string FileLabel, string SnapshotPath, string? OriginalPath, BitmapSource? Thumbnail, bool IsResult);

public partial class EditorMaterialPicker : UserControl
{
    private readonly DispatcherTimer _closeTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private Window? _owner;
    private bool _keyboardOpen;
    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint { public int X; public int Y; }
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out ScreenPoint point);
    public event Action<EditorMaterial>? MaterialSelected;
    public bool IsOpen => materialPopup.IsOpen;
    public int SourceCount => materialSources.Children.Count;
    public int ResultCount => materialResults.Children.Count;

    public EditorMaterialPicker()
    {
        InitializeComponent();
        materialTrigger.MouseEnter += (_, _) => Open();
        materialTrigger.MouseLeave += (_, _) => ScheduleClose();
        materialTrigger.Click += (_, _) => Open();
        materialTrigger.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
            if (e.Key != Key.Down) return;
            Open(keyboard: true);
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (IsOpen && _keyboardOpen) FirstCard()?.Focus();
            }));
            e.Handled = true;
        };
        materialPopup.Closed += (_, _) => _closeTimer.Stop();
        materialPanel.MouseEnter += (_, _) => _closeTimer.Stop();
        materialPanel.MouseLeave += (_, _) => ScheduleClose();
        materialPanel.LostKeyboardFocus += (_, _) => ScheduleClose();
        materialPanel.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            materialTrigger.Focus();
            e.Handled = true;
        };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            if (!CursorOver(materialTrigger) && !CursorOver(materialPanel) && !(_keyboardOpen && materialPanel.IsKeyboardFocusWithin)) Close();
        };
        Loaded += (_, _) =>
        {
            _owner = Window.GetWindow(this);
            if (_owner is null) return;
            _owner.Deactivated += OwnerDeactivated;
            _owner.PreviewMouseDown += OwnerMouseDown;
        };
        Unloaded += (_, _) =>
        {
            Close();
            if (_owner is null) return;
            _owner.Deactivated -= OwnerDeactivated;
            _owner.PreviewMouseDown -= OwnerMouseDown;
            _owner = null;
        };
    }

    public void SetMaterials(IEnumerable<EditorMaterial> materials)
    {
        Close();
        materialSources.Children.Clear();
        materialResults.Children.Clear();
        foreach (var material in materials)
        {
            var card = CreateCard(material);
            if (material.IsResult) materialResults.Children.Add(card);
            else materialSources.Children.Add(card);
        }
        materialResultsTitle.Visibility = materialResults.Visibility = ResultCount == 0 ? Visibility.Collapsed : Visibility.Visible;
        Visibility = SourceCount + ResultCount == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private Button CreateCard(EditorMaterial material)
    {
        var card = new Button { Width = 96, Padding = new Thickness(6), Margin = new Thickness(0, 0, 8, 8), Tag = material,
            ToolTip = material.Label };
        System.Windows.Automation.AutomationProperties.SetName(card, material.Label);
        var content = new StackPanel();
        var image = new Image { Source = material.Thumbnail, Stretch = Stretch.Uniform, Width = 72, Height = 72 };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        content.Children.Add(new Border { Width = 72, Height = 72, Background = (Brush)FindResource("MaterialChecker"), Child = image });
        content.Children.Add(new TextBlock { Text = material.Label, Width = 80, TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 7, 0, 0), FontSize = 11 });
        card.Content = content;
        card.Click += (_, _) => { Close(); MaterialSelected?.Invoke(material); };
        return card;
    }

    private Button? FirstCard() => materialSources.Children.OfType<Button>().FirstOrDefault() ?? materialResults.Children.OfType<Button>().FirstOrDefault();

    private static bool CursorOver(FrameworkElement element)
    {
        if (!element.IsVisible || !GetCursorPos(out var cursor)) return false;
        var point = element.PointFromScreen(new Point(cursor.X, cursor.Y));
        return point.X >= 0 && point.Y >= 0 && point.X < element.ActualWidth && point.Y < element.ActualHeight;
    }
    private void Open(bool keyboard = false)
    {
        if (Visibility != Visibility.Visible || !IsEnabled) return;
        _closeTimer.Stop();
        if (!IsOpen) _keyboardOpen = false;
        _keyboardOpen |= keyboard;
        materialPopup.IsOpen = true;
    }

    private void ScheduleClose()
    {
        if (!IsOpen) return;
        _closeTimer.Stop();
        _closeTimer.Start();
    }

    private void Close()
    {
        _closeTimer.Stop();
        _keyboardOpen = false;
        materialPopup.IsOpen = false;
    }

    private void OwnerDeactivated(object? sender, EventArgs e) => Close();

    private void OwnerMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsOpen && !CursorOver(materialTrigger) && !CursorOver(materialPanel)) Close();
    }
}
