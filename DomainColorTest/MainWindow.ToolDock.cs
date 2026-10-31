using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DomainColorTest;

public partial class MainWindow
{
    private const string ToolDragFormat = "Tessera.ToolWidget";
    private TabControl? _workspaceTabs;
    private TextBlock? _dockEmptyHint;
    private readonly Dictionary<string, DockedTool> _dockedTools = [];

    private sealed class DockedTool(string key, string title, GroupBox[] groups, StackPanel content, TabItem tab)
    {
        public string Key { get; } = key;
        public string Title { get; } = title;
        public GroupBox[] Groups { get; } = groups;
        public StackPanel Content { get; } = content;
        public TabItem Tab { get; } = tab;
    }

    private void InitializeToolDock()
    {
        if (_workspaceDock is null) return;
        _workspaceTabs = new TabControl { Style = (Style)FindResource("ToolDockTabs") };
        _dockEmptyHint = new TextBlock { Text = Localization.Text("Перетащите инструмент сюда"),
            TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16) };
        _dockEmptyHint.SetResourceReference(TextBlock.ForegroundProperty, "Ts.TextSecondary");
        var layout = new Grid();
        layout.Children.Add(_workspaceTabs);
        layout.Children.Add(_dockEmptyHint);
        _workspaceTabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, _workspaceTabs)) return;
            ActivateDockedTool((_workspaceTabs.SelectedItem as TabItem)?.Tag as string);
        };
        _workspaceDock.Child = layout;
        _workspaceDock.AllowDrop = true;
        _workspaceDock.PreviewDragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(ToolDragFormat)) return;
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        };
        _workspaceDock.PreviewDrop += (_, e) =>
        {
            if (e.Data.GetData(ToolDragFormat) is not string key) return;
            if (_toolWindows.ContainsKey(key)) DockFloatingTool(key);
            else if (_dockedTools.TryGetValue(key, out var docked)) _workspaceTabs.SelectedItem = docked.Tab;
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        };
        RefreshDockEmptyState();
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
        var content = new StackPanel { Margin = new Thickness(14), IsEnabled = !_processing };
        for (int q = 0; q < groups.Length; q++)
        {
            var group = groups[q];
            if (!group.Resources.MergedDictionaries.Contains(Resources)) group.Resources.MergedDictionaries.Add(Resources);
            DetachToolGroup(group);
            _workspaceGroups.Add(group);
            if (q == 0) content.Children.Add(group);
            else content.Children.Add(new Expander { Header = group.Header, Content = group, IsExpanded = false,
                Margin = new Thickness(0, 8, 0, 0), Style = (Style)FindResource("ToolExpander") });
        }
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        var close = new Button { Content = "×", Width = 20, Height = 20, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
            ToolTip = Localization.Text("Закрыть инструмент") };
        close.Click += (_, e) => { e.Handled = true; CloseDockedTool(key); };
        Grid.SetColumn(close, 2); header.Children.Add(close);
        var grip = new TextBlock { Text = "⋮⋮", VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.SizeAll,
            ToolTip = Localization.Text("Перетащите инструмент из панели") };
        Grid.SetColumn(grip, 0); header.Children.Add(grip);
        string tabTitle = key switch { "size" => Localization.Text("Размер"), "finalists" => "TCR", _ => title };
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
        _workspaceDock.Visibility = Visibility.Visible;
        RefreshDockEmptyState();
        UpdateWorkspaceDockLayout();
        ActivateDockedTool(key);
        UpdateToolOpenState();
    }

    private void CloseDockedTool(string key, bool transfer = false)
    {
        if (!_dockedTools.Remove(key, out var tool) || _workspaceTabs is null) return;
        _workspaceTabs.Items.Remove(tool.Tab);
        tool.Tab.Content = null;
        foreach (var group in tool.Groups)
        {
            DetachToolGroup(group);
            _workspaceGroups.Remove(group);
            if (!transfer) settingsPanel.Children.Add(group);
        }
        if (!transfer && key == "info") ResetInfoSnapshot();
        RefreshDockEmptyState();
        if (_workspaceTabs.SelectedItem is TabItem selected) ActivateDockedTool(selected.Tag as string);
        else ActivateDockedTool(null);
        UpdateWorkspaceDockLayout();
        UpdateToolOpenState();
    }

    private void ActivateDockedTool(string? key)
    {
        _cropToolActive = key == "size";
        UpdateCropPreview();
        SetActiveLivePreviewTool(key);
        if (key == "info") RefreshInfo();
    }

    private void RefreshDockEmptyState()
    {
        if (_workspaceTabs is null || _dockEmptyHint is null) return;
        bool empty = _workspaceTabs.Items.Count == 0;
        _workspaceTabs.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        _dockEmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty && _workspaceDock is not null)
            _workspaceDock.Visibility = _workspaceReady && ImageEditorVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void DetachToolGroup(GroupBox group)
    {
        if (group.Parent is Panel panel) panel.Children.Remove(group);
        else if (group.Parent is Expander expander) expander.Content = null;
        else if (group.Parent is ContentControl content) content.Content = null;
    }
}
