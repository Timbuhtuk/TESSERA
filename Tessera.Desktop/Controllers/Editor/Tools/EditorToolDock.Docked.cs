using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Tessera.Desktop;

internal sealed partial class EditorToolDock
{
    private const string ToolDragFormat = "Tessera.ToolWidget";

    private Border? _workspaceDock;
    private TabControl? _workspaceTabs;

    private readonly Dictionary<string, DockedTool> _dockedTools = [];

    internal void OpenDefaultSizeTool()
        => DockTool("size", Localization.Text("Size"), [_editor.sizeToolGroup, _editor.frameToolGroup, _editor.pixelToolGroup]);

    private void CloseDockedWorkspaceTool()
    {
        foreach (string key in _dockedTools.Keys.ToArray()) CloseDockedTool(key);
    }

    private sealed class DockedTool(string key, string title, GroupBox[] groups, StackPanel content, TabItem tab)
    {
        public string Key { get; } = key;
        public string Title { get; } = title;
        public GroupBox[] Groups { get; } = groups;
        public StackPanel Content { get; } = content;
        public TabItem Tab { get; } = tab;
    }

    internal void InitializeEditorDock()
    {
        _editor.workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition());
        _editor.workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _editor.workspaceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _editor.workspaceGrid.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(_editor.previewArea, 1);
        _workspaceDock = new Border { Width = 320, BorderThickness = new Thickness(1, 0, 0, 0), Visibility = Visibility.Collapsed };
        _workspaceDock.SetResourceReference(Border.BackgroundProperty, "Ts.Raised");
        _workspaceDock.SetResourceReference(Border.BorderBrushProperty, "Ts.Border");
        Grid.SetColumn(_workspaceDock, 1);
        Grid.SetRow(_workspaceDock, 1);
        _editor.workspaceGrid.Children.Add(_workspaceDock);
        _editor.workspaceGrid.AllowDrop = true;
        InitializeToolDock();
        _editor.SizeChanged += (_, _) => UpdateWorkspaceDockLayout();
    }

    private void InitializeToolDock()
    {
        if (_workspaceDock is null) return;
        _workspaceTabs = new TabControl { Style = (Style)_editor.FindResource("ToolDockTabs") };
        _workspaceTabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, _workspaceTabs)) return;
            ActivateDockedTool((_workspaceTabs.SelectedItem as TabItem)?.Tag as string);
        };
        _workspaceDock.Child = _workspaceTabs;
    }

    internal bool HandleToolDrop(DragEventArgs e, bool commit)
    {
        if (!e.Data.GetDataPresent(ToolDragFormat)) return false;
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (_workspaceDock is null || _workspaceTabs is null || !_editor.ImageEditorVisible || Host.LibraryVisible ||
            e.Data.GetData(ToolDragFormat) is not string key) return true;
        if (!new Rect(_editor.workspaceHost.RenderSize).Contains(e.GetPosition(_editor.workspaceHost))) return true;

        bool dockVisible = _workspaceDock.IsVisible;
        FrameworkElement target = dockVisible ? _workspaceDock : _editor.workspaceGrid;
        Point point = e.GetPosition(target);
        double left = dockVisible ? 0 : Math.Max(0, target.ActualWidth - 48);
        if (point.X < left || point.X >= target.ActualWidth || point.Y < 0 || point.Y >= target.ActualHeight)
            return true;

        if (_toolWindows.ContainsKey(key))
        {
            if (commit) DockFloatingTool(key);
        }
        else if (_dockedTools.TryGetValue(key, out var docked))
        {
            if (commit) _workspaceTabs.SelectedItem = docked.Tab;
        }
        else return true;

        e.Effects = DragDropEffects.Move;
        return true;
    }

    private void DockTool(string key, string title, GroupBox[] groups)
    {
        if (_workspaceDock is null || _workspaceTabs is null) return;
        if (_dockedTools.TryGetValue(key, out var opened))
        {
            _workspaceTabs.SelectedItem = opened.Tab;
            ActivateDockedTool(key);
            return;
        }
        var content = new StackPanel { Margin = new Thickness(14), IsEnabled = !_editor._processing };
        for (int q = 0; q < groups.Length; q++)
        {
            var group = groups[q];
            PrepareToolGroup(group);
            if (q == 0) content.Children.Add(group);
            else content.Children.Add(new Expander { Header = group.Header, Content = group, IsExpanded = false,
                Margin = new Thickness(0, 8, 0, 0), Style = (Style)_editor.FindResource("ToolExpander") });
        }
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        var cross = new Path
        {
            Data = Geometry.Parse("M 0,0 L 7,7 M 7,0 L 0,7"), Width = 8, Height = 8,
            Stretch = Stretch.Uniform, StrokeThickness = 1.4, IsHitTestVisible = false
        };
        cross.SetResourceReference(Shape.StrokeProperty, "Ts.TextSecondary");
        string closeLabel = Localization.Text("CloseTool");
        var close = new Button { Content = cross, Style = (Style)_editor.FindResource("CaptionButton"),
            Width = 20, Height = 20, FocusVisualStyle = null, ToolTip = closeLabel };
        AutomationProperties.SetName(close, closeLabel);
        close.Click += (_, e) => { e.Handled = true; CloseDockedTool(key); };
        Grid.SetColumn(close, 2); header.Children.Add(close);
        var grip = new TextBlock { Text = "⋮⋮", VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.SizeAll,
            ToolTip = Localization.Text("DragToolOutOfTheDock") };
        Grid.SetColumn(grip, 0); header.Children.Add(grip);
        if (key == TileAtlasPage.ToolKey) grip.Visibility = Visibility.Collapsed;
        string tabTitle = key switch { "size" => Localization.Text("Size"), "finalists" => "TCR", _ => title };
        header.ToolTip = title;
        var label = new TextBlock { Text = tabTitle, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        Grid.SetColumn(label, 1); header.Children.Add(label);
        Point dragStart = default;
        grip.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragStart = e.GetPosition(grip);
            grip.CaptureMouse();
        };
        grip.PreviewMouseLeftButtonUp += (_, _) => grip.ReleaseMouseCapture();
        grip.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            Point current = e.GetPosition(grip);
            if (Math.Abs(current.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            grip.ReleaseMouseCapture();
            var result = DragDrop.DoDragDrop(grip, new DataObject(ToolDragFormat, key), DragDropEffects.Move);
            if (result == DragDropEffects.None && _dockedTools.ContainsKey(key)) FloatDockedTool(key, center: false);
        };
        var tab = new TabItem { Header = header, Tag = key, Content = new ScrollViewer
        {
            Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        } };
        _dockedTools.Add(key, new DockedTool(key, title, groups, content, tab));
        _workspaceTabs.Items.Add(tab);
        _workspaceTabs.SelectedItem = tab;
        UpdateWorkspaceDockLayout();
        ActivateDockedTool(key);
        Host.UpdateToolOpenState();
    }

    internal void CloseDockedTool(string key, bool transfer = false)
    {
        if (!_dockedTools.Remove(key, out var tool) || _workspaceTabs is null) return;
        _workspaceTabs.Items.Remove(tool.Tab);
        tool.Tab.Content = null;
        foreach (var group in tool.Groups)
        {
            DetachToolGroup(group);
            if (!transfer) _editor.settingsPanel.Children.Add(group);
        }
        if (!transfer && key == "info") _editor.Info.ResetInfoSnapshot();
        if (_workspaceTabs.SelectedItem is TabItem selected) ActivateDockedTool(selected.Tag as string);
        else ActivateDockedTool(null);
        UpdateWorkspaceDockLayout();
        Host.UpdateToolOpenState();
    }

    private void ActivateDockedTool(string? key)
    {
        _editor.ActivateAtlasTool(key == TileAtlasPage.ToolKey);
        _editor.Preview.CropToolActive = key == "size";
        _editor.Preview.UpdateCropPreview();
        _editor.Preview.SetActiveLivePreviewTool(key);
        if (key == "info") _editor.Info.RefreshInfo();
    }

    private void UpdateWorkspaceDockLayout()
    {
        if (_workspaceDock is null) return;
        bool compact = Host.ActualWidth < 1000;
        _workspaceDock.Width = compact ? double.NaN : 320;
        _workspaceDock.MaxHeight = compact ? 260 : double.PositiveInfinity;
        Grid.SetColumn(_workspaceDock, compact ? 0 : 1);
        Grid.SetRow(_workspaceDock, compact ? 0 : 1);
        _workspaceDock.Visibility = _dockedTools.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PrepareToolGroup(GroupBox group, bool showHeader = false)
    {
        // Keep control styles in scope during reparenting so sliders retain their ranges and values.
        if (!group.Resources.MergedDictionaries.Contains(_editor.Resources)) group.Resources.MergedDictionaries.Add(_editor.Resources);
        DetachToolGroup(group);
        group.Style = (Style)_editor.FindResource(showHeader ? "ToolSection" : "ToolGroup");
    }

    private static void DetachToolGroup(GroupBox group)
    {
        if (group.Parent is Panel panel) panel.Children.Remove(group);
        else if (group.Parent is Expander expander) expander.Content = null;
        else if (group.Parent is ContentControl content) content.Content = null;
    }
}
