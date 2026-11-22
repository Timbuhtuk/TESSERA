using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Shell;
using System.Runtime.InteropServices;

namespace Tessera.Desktop;

internal sealed partial class EditorToolDock
{
    private readonly ImageEditorPage _editor;
    private MainWindow Host => _editor.Host;

    internal EditorToolDock(ImageEditorPage editor)
    {
        _editor = editor;
    }

    private sealed record ToolDefinition(string Title, double Height, GroupBox[] Groups);

    private readonly Dictionary<string, ToolDefinition> _toolDefinitions = [];

    private readonly Dictionary<string, EditorToolWindow> _toolWindows = [];

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    internal void SetControlsEnabled(bool enabled, bool macroRunning)
    {
        foreach (var tool in _dockedTools.Values) tool.Content.IsEnabled = enabled || macroRunning;
        foreach (var tool in _toolWindows.Values) tool.ToolContent.IsEnabled = enabled;
    }

    internal bool IsToolOpen(string key) => _dockedTools.ContainsKey(key) || _toolWindows.ContainsKey(key);
    internal bool IsShownInDock(string key) => _workspaceDock?.Visibility == Visibility.Visible && _dockedTools.ContainsKey(key);
    internal Window? FloatingWindow(string key) => _toolWindows.GetValueOrDefault(key);

    internal void CloseTool(string key)
    {
        CloseDockedTool(key);
        FloatingWindow(key)?.Close();
    }

    private static Point ScreenCursor() => GetCursorPos(out var point) ? new Point(point.X, point.Y) : new Point(double.NaN, double.NaN);

    internal void RegisterTool(string key, string title)
    {
        (double Height, GroupBox[] Groups) tool = key switch
        {
            "size" => (370, [_editor.sizeToolGroup, _editor.frameToolGroup, _editor.pixelToolGroup]),
            "grid" => (400, [_editor.gridToolGroup]),
            "finalists" => (460, [_editor.Tools.FinalistGroup]),
            "color" => (580, [_editor.colorToolGroup]),
            "smoothing" => (470, [_editor.smoothingToolGroup]),
            "channel" => (460, [_editor.channelToolGroup]),
            "info" => (760, [_editor.infoToolGroup]),
            "monochrome" => (300, [_editor.monochromeToolGroup]),
            "sharpen" => (390, [_editor.sharpenToolGroup]),
            "ascii" => (640, [_editor.asciiToolGroup]),
            "macros" => (460, [_editor.History.MacrosGroup]),
            "contours" => (580, [_editor.contourToolGroup]),
            "gridLens" => (450, [_editor.Preview.GridLensGroup]),
            "tileHeatmapLens" => (560, [_editor.Preview.TileHeatmapLensGroup]),
            TileAtlasPage.ToolKey => (620, [_editor.Atlas.SettingsGroup]),
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
        _toolDefinitions.Add(key, new ToolDefinition(title, tool.Height, tool.Groups));
    }

    internal void OpenRegisteredTool(string key)
    {
        var definition = _toolDefinitions[key];
        OpenEditorTool(key, definition.Title, definition.Height, definition.Groups);
    }

    internal void OpenEditorTool(string key, string title, double height, params GroupBox[] groups)
        => OpenEditorToolCore(key, title, height, groups);

    private void OpenEditorToolCore(string key, string title, double height, GroupBox[] groups,
        bool forceFloating = false, bool centerFloating = true)
    {
        if (key == TileAtlasPage.ToolKey)
        {
            if (!_editor.ImageEditorVisible || Host.WorkspaceBusy) return;
            if (_editor.Preview.AlternativePreview) _editor.Preview.AlternativePreview = false;
            foreach (var lens in AppearanceSettings.Lenses) _editor.Preview.CloseLens(lens.Tool);
            DockTool(key, title, groups);
            return;
        }
        if (AppearanceSettings.Lenses.Any(lens => lens.Tool == key))
        {
            if (_editor.Preview.AlternativePreview)
            {
                Host.Notify(Localization.Text("LensesAreUnavailableInAlternativePreview"),
                    NotificationKind.Warning);
                return;
            }
            foreach (var lens in AppearanceSettings.Lenses)
                if (lens.Tool != key) _editor.Preview.CloseLens(lens.Tool);
        }
        if (key == "contours") _editor.Tools.UpdateContourWindowLabel();
        if (_dockedTools.ContainsKey(key) && !forceFloating)
        {
            FloatDockedTool(key, center: true);
            if (key == "info") _editor.Info.RefreshInfo();
            return;
        }
        if (_toolWindows.TryGetValue(key, out var existing))
        {
            CenterFloatingWindow(existing);
            SelectToolPreview(key);
            if (key == "info") _editor.Info.RefreshInfo();
            return;
        }
        if (Host._workspaceReady && !forceFloating)
        {
            DockTool(key, title, groups);
            if (key is "color" or "smoothing") _editor.Preview.AutoEnableLivePreview(key);
            return;
        }
        SelectToolPreview(key);

        var content = new StackPanel { Margin = new Thickness(16, 16, 16, 4), IsEnabled = !_editor._processing };
        StackPanel? advancedContent = groups.Length > 1 ? new StackPanel() : null;
        for (int index = 0; index < groups.Length; index++)
        {
            var group = groups[index];
            PrepareToolGroup(group, showHeader: index > 0);
            if (index == 0) content.Children.Add(group);
            else advancedContent!.Children.Add(group);
        }
        if (advancedContent is not null)
            content.Children.Add(new Expander
            {
                Header = Localization.Text("AdvancedSettings"),
                Content = advancedContent,
                Style = (Style)_editor.FindResource("ToolExpander"),
                Margin = new Thickness(0, 2, 0, 8)
            });

        var tool = new EditorToolWindow(_editor, key, title, content, height);
        tool.Activated += (_, _) => SelectToolPreview(key);
        tool.Closed += (_, _) =>
        {
            _toolWindows.Remove(key);
            if (tool.ReturningToDock) { Host.UpdateToolOpenState(); return; }
            if (_editor.Preview.CropToolActive && key == "size") { _editor.Preview.CropToolActive = false; _editor.Preview.UpdateCropPreview(); }
            if (_editor.Preview.ActiveLivePreviewTool == key) _editor.Preview.SetActiveLivePreviewTool(null);
            if (key == "info") _editor.Info.ResetInfoSnapshot();
            foreach (var group in groups)
            {
                DetachToolGroup(group);
                _editor.settingsPanel.Children.Add(group);
            }
            Host.UpdateToolOpenState();
        };
        _toolWindows.Add(key, tool);
        tool.Show();
        if (forceFloating)
        {
            if (centerFloating) CenterFloatingWindow(tool);
            else
            {
                var point = ScreenCursor();
                if (double.IsFinite(point.X) && double.IsFinite(point.Y))
                {
                    var area = SystemParameters.WorkArea;
                    tool.Left = Math.Clamp(point.X - 80, area.Left, Math.Max(area.Left, area.Right - tool.Width));
                    tool.Top = Math.Clamp(point.Y - 20, area.Top, Math.Max(area.Top, area.Bottom - tool.Height));
                }
                else CenterFloatingWindow(tool);
                tool.Activate();
            }
        }
        if (key is "color" or "smoothing") _editor.Preview.AutoEnableLivePreview(key);
        if (key == "info") _editor.Info.RefreshInfo();
        Host.UpdateToolOpenState();
    }

    private void SelectToolPreview(string key)
    {
        _editor.Preview.CropToolActive = key == "size";
        _editor.Preview.UpdateCropPreview();
        _editor.Preview.SetActiveLivePreviewTool(key);
    }

    private void FloatDockedTool(string key, bool center)
    {
        if (key == TileAtlasPage.ToolKey || !_dockedTools.ContainsKey(key) || !_toolDefinitions.TryGetValue(key, out var definition)) return;
        CloseDockedTool(key, transfer: true);
        OpenEditorToolCore(key, definition.Title, definition.Height, definition.Groups, forceFloating: true, centerFloating: center);
    }

    private void DockFloatingTool(string key)
    {
        if (!_toolWindows.TryGetValue(key, out var tool) || !_toolDefinitions.TryGetValue(key, out var definition)) return;
        tool.ReturningToDock = true;
        tool.Close();
        DockTool(key, definition.Title, definition.Groups);
    }

    private static void CenterFloatingWindow(EditorToolWindow tool)
    {
        if (tool.WindowState == WindowState.Minimized) tool.WindowState = WindowState.Normal;
        var area = SystemParameters.WorkArea;
        tool.Left = area.Left + (area.Width - tool.Width) / 2;
        tool.Top = area.Top + (area.Height - tool.Height) / 2;
        tool.Activate();
    }

    internal void CloseEditorTools()
    {
        CloseDockedWorkspaceTool();
        foreach (var tool in _toolWindows.Values.ToArray()) tool.Close();
        Host.CloseNavigationMenus();
    }

    private sealed class EditorToolWindow : Window
    {
        public StackPanel ToolContent { get; }
        public bool ReturningToDock { get; set; }

        public EditorToolWindow(ImageEditorPage owner, string key, string title, StackPanel content, double height)
        {
            Owner = owner.Host;
            Title = title + " · TESSERA";
            Icon = owner.Host.Icon;
            Width = 440;
            Height = Math.Min(height, SystemParameters.WorkArea.Height - 60);
            MinWidth = 360;
            MinHeight = 230;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "Ts.Canvas");
            SetResourceReference(ForegroundProperty, "Ts.Text");
            FontFamily = (FontFamily)owner.FindResource("Ts.UiFont");
            FontSize = 13;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Resources.MergedDictionaries.Add(owner.Resources);
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 38,
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });

            PreviewKeyDown += (_, e) =>
            {
                if (Keyboard.Modifiers != ModifierKeys.Control) return;
                if (e.Key == Key.O) owner.Host.OpenImage(this, EventArgs.Empty);
                else if (e.Key == Key.S && owner.Host.CanSaveImage) owner.SaveImage(this, EventArgs.Empty);
                else return;
                e.Handled = true;
            };
            owner.Preview.BindComparisonKey(this);
            PreviewKeyDown += owner.Preview.LensKeyDown;

            ToolContent = content;
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition());

            var titleBar = new Grid
            {
                Height = 38
            };
            titleBar.SetResourceReference(Panel.BackgroundProperty, "Ts.Raised");
            titleBar.ColumnDefinitions.Add(new ColumnDefinition());
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleBar.Children.Add(new TextBlock
            {
                Text = title,
                FontFamily = (FontFamily)owner.FindResource("Ts.DisplayFont"),
                Margin = new Thickness(16, 0, 0, 0),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            var dock = new Button
            {
                Content = "⇤", Width = 38, Height = 38, FontSize = 17,
                ToolTip = Localization.Text("DragToTheDock"),
                Style = (Style)FindResource("Ts.CaptionButton")
            };
            dock.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Left) return;
                e.Handled = true;
                Point start = ScreenCursor();
                var result = DragDrop.DoDragDrop(dock, new DataObject(ToolDragFormat, key), DragDropEffects.Move);
                if (result != DragDropEffects.None || !owner.Dock._toolWindows.ContainsKey(key)) return;
                Point end = ScreenCursor();
                if (double.IsFinite(end.X) && double.IsFinite(end.Y) && owner.Dock._workspaceDock is { IsVisible: true } host)
                {
                    Point drop = host.PointFromScreen(end);
                    if (drop.X >= 0 && drop.Y >= 0 && drop.X < host.ActualWidth && drop.Y < host.ActualHeight)
                    {
                        owner.Dock.DockFloatingTool(key);
                        return;
                    }
                }
                if (!double.IsFinite(start.X) || !double.IsFinite(end.X) ||
                    Math.Abs(end.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(end.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
                    owner.Dock.DockFloatingTool(key);
            };
            dock.Click += (_, _) => owner.Dock.DockFloatingTool(key);
            WindowChrome.SetIsHitTestVisibleInChrome(dock, true);
            Grid.SetColumn(dock, 1); titleBar.Children.Add(dock);
            var close = new Button
            {
                Content = "×",
                Width = 42,
                Height = 38,
                FontSize = 19,
                ToolTip = Localization.Text("Close"),
                Style = (Style)FindResource("Ts.CloseCaptionButton")
            };
            close.Click += (_, _) => Close();
            WindowChrome.SetIsHitTestVisibleInChrome(close, true);
            Grid.SetColumn(close, 2);
            titleBar.Children.Add(close);
            layout.Children.Add(titleBar);

            var scroll = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(scroll, 1);
            layout.Children.Add(scroll);
            Content = layout;
        }
    }
}
