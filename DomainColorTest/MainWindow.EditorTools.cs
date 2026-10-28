using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Shell;
using System.Runtime.InteropServices;

namespace DomainColorTest;

public partial class MainWindow
{
    private readonly Dictionary<string, EditorToolWindow> _toolWindows = [];
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);
    private static Point ScreenCursor() => GetCursorPos(out var point) ? new Point(point.X, point.Y) : new Point(double.NaN, double.NaN);

    private void OpenEditorTool(string key, string title, double height, params GroupBox[] groups)
        => OpenEditorToolCore(key, title, height, groups);

    private void OpenEditorToolCore(string key, string title, double height, GroupBox[] groups,
        bool forceFloating = false, bool centerFloating = true)
    {
        if (key is "contours" or "gridLens")
        {
            if (AlternativePreview)
            {
                TesseraDialog.Show(this, Localization.Text("Линзы не работают в альтернативном просмотре. Выключите его, чтобы открыть линзу."),
                    Localization.Text("Линзы"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string other = key == "contours" ? "gridLens" : "contours";
            CloseDockedTool(other);
            if (_toolWindows.TryGetValue(other, out var otherWindow)) otherWindow.Close();
        }
        if (key == "contours") UpdateContourWindowLabel();
        if (_dockedTools.ContainsKey(key) && !forceFloating)
        {
            FloatDockedTool(key, center: true);
            if (key == "info") RefreshInfo();
            return;
        }
        if (_toolWindows.TryGetValue(key, out var existing))
        {
            CenterFloatingWindow(existing);
            if (key == "info") RefreshInfo();
            return;
        }
        if (_workspaceReady && !forceFloating)
        {
            OpenDockedWorkspaceTool(key, title, groups);
            if (key is "color" or "smoothing") AutoEnableLivePreview(key);
            return;
        }
        _cropToolActive = key == "size";
        UpdateCropPreview();
        SetActiveLivePreviewTool(key);

        var content = new StackPanel { Margin = new Thickness(16, 16, 16, 4), IsEnabled = !_processing };
        StackPanel? advancedContent = groups.Length > 1 ? new StackPanel() : null;
        for (int index = 0; index < groups.Length; index++)
        {
            var group = groups[index];
            // Сохраняем стили при переносе: иначе Slider временно получает Maximum = 10 и меняет Value.
            if (!group.Resources.MergedDictionaries.Contains(Resources)) group.Resources.MergedDictionaries.Add(Resources);
            DetachToolGroup(group);
            if (index == 0) content.Children.Add(group);
            else advancedContent!.Children.Add(group);
        }
        if (advancedContent is not null)
            content.Children.Add(new Expander
            {
                Header = Localization.Text("Дополнительные настройки"),
                Content = advancedContent,
                Style = (Style)FindResource("ToolExpander"),
                Margin = new Thickness(0, 2, 0, 8)
            });

        var tool = new EditorToolWindow(this, key, title, content, height);
        tool.Activated += (_, _) => { _cropToolActive = key == "size"; UpdateCropPreview(); SetActiveLivePreviewTool(key); };
        tool.Closed += (_, _) =>
        {
            _toolWindows.Remove(key);
            if (tool.ReturningToDock) { UpdateToolOpenState(); return; }
            if (_cropToolActive && key == "size") { _cropToolActive = false; UpdateCropPreview(); }
            if (ActiveLivePreviewTool == key) SetActiveLivePreviewTool(null);
            if (key == "info") ResetInfoSnapshot();
            foreach (var group in groups)
            {
                DetachToolGroup(group);
                settingsPanel.Children.Add(group);
            }
            UpdateToolOpenState();
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
                    var local = PointFromScreen(point);
                    point = new Point(Left + local.X, Top + local.Y);
                    var area = SystemParameters.WorkArea;
                    tool.Left = Math.Clamp(point.X - 80, area.Left, Math.Max(area.Left, area.Right - tool.Width));
                    tool.Top = Math.Clamp(point.Y - 20, area.Top, Math.Max(area.Top, area.Bottom - tool.Height));
                }
                else CenterFloatingWindow(tool);
                tool.Activate();
            }
        }
        if (key is "color" or "smoothing") AutoEnableLivePreview(key);
        if (key == "info") RefreshInfo();
        UpdateToolOpenState();
    }

    private void FloatDockedTool(string key, bool center)
    {
        if (!_dockedTools.ContainsKey(key) || !_toolDefinitions.TryGetValue(key, out var definition)) return;
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

    private void CloseEditorTools()
    {
        CloseDockedWorkspaceTool();
        foreach (var tool in _toolWindows.Values.ToArray()) tool.Close();
        CloseNavigationMenus();
    }

    private sealed class EditorToolWindow : Window
    {
        public StackPanel ToolContent { get; }
        public bool ReturningToDock { get; set; }

        public EditorToolWindow(MainWindow owner, string key, string title, StackPanel content, double height)
        {
            Owner = owner;
            Title = title + " · TESSERA";
            Icon = owner.Icon;
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
            FontFamily = (FontFamily)owner.FindResource("UiFont");
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
                if (e.Key == Key.O) owner.OpenImage(this, EventArgs.Empty);
                else if (e.Key == Key.S && owner.saveMenuItem.IsEnabled) owner.SaveImage(this, EventArgs.Empty);
                else return;
                e.Handled = true;
            };

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
                FontFamily = (FontFamily)owner.FindResource("DisplayFont"),
                Margin = new Thickness(16, 0, 0, 0),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            var dock = new Button
            {
                Content = "⇤", Width = 38, Height = 38, FontSize = 17,
                ToolTip = Localization.Text("Перетащите в правую панель или нажмите для закрепления"),
                Style = (Style)FindResource("CaptionButton")
            };
            dock.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Left) return;
                e.Handled = true;
                Point start = ScreenCursor();
                bool cancelled = false;
                QueryContinueDragEventHandler onContinue = (_, args) => cancelled |= args.EscapePressed;
                DragDropEffects result;
                dock.QueryContinueDrag += onContinue;
                try { result = DragDrop.DoDragDrop(dock, new DataObject(ToolDragFormat, key), DragDropEffects.Move); }
                finally { dock.QueryContinueDrag -= onContinue; }
                if (cancelled || result != DragDropEffects.None || !owner._toolWindows.ContainsKey(key)) return;
                Point end = ScreenCursor();
                if (double.IsFinite(end.X) && double.IsFinite(end.Y) && owner._workspaceDock is { IsVisible: true } host)
                {
                    Point drop = host.PointFromScreen(end);
                    if (drop.X >= 0 && drop.Y >= 0 && drop.X < host.ActualWidth && drop.Y < host.ActualHeight)
                    {
                        owner.DockFloatingTool(key);
                        return;
                    }
                }
                if (!double.IsFinite(start.X) || !double.IsFinite(end.X) ||
                    Math.Abs(end.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(end.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
                    owner.DockFloatingTool(key);
            };
            dock.Click += (_, _) => owner.DockFloatingTool(key);
            WindowChrome.SetIsHitTestVisibleInChrome(dock, true);
            Grid.SetColumn(dock, 1); titleBar.Children.Add(dock);
            var close = new Button
            {
                Content = "×",
                Width = 42,
                Height = 38,
                FontSize = 19,
                ToolTip = Localization.Text("Закрыть"),
                Style = (Style)FindResource("CloseCaptionButton")
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
