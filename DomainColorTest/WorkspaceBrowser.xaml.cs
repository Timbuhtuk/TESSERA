using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Automation;
using System.Xml.Linq;

namespace DomainColorTest;

public partial class WorkspaceBrowser : UserControl
{
    private WorkspaceCatalog? _catalog;
    private bool _ready, _building;
    private Point _dragStart;
    private object? _treeDrag;
    private WorkspaceAsset? _deferredClick;
    public const string AssetDragFormat = "Tessera.WorkspaceAssets";
    private const string FolderDragFormat = "Tessera.WorkspaceFolder";
    public event Action<IReadOnlyList<string>>? ImportRequested;
    public event Action<WorkspaceAsset>? OpenRequested;
    public event Action<string, WorkspaceAsset?>? ActionRequested;
    public event Action<string>? StatusChanged;
    public event Action? ConnectUnrealRequested;
    public IReadOnlyList<WorkspaceAsset> Selection => assetList.SelectedItems.Cast<WorkspaceAsset>().ToArray();

    public WorkspaceBrowser()
    {
        InitializeComponent();
        connectUnrealButton.Content = new TextBlock { Text = Localization.Text("Подключить проект Unreal…"), TextWrapping = TextWrapping.Wrap };
        assetViewLabel.Text = Localization.Text("Вид");
        assetViewInput.ItemsSource = new[] { Localization.Text("Плитка"), Localization.Text("Список"), Localization.Text("Мелкая плитка") };
        assetViewInput.ToolTip = Localization.Text("Режим отображения ассетов");
        AutomationProperties.SetName(assetViewInput, Localization.Text("Режим отображения ассетов"));
        SizeChanged += (_, _) =>
        {
            bool compact = ActualWidth < 1050;
            detailColumn.Width = new GridLength(compact ? 0 : 270);
            detailsBorder.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        };
        PreviewDragOver += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; }
        };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) { ImportRequested?.Invoke(files); e.Handled = true; }
        };
        var context = new ContextMenu();
        foreach (var pair in new[] { (Localization.Text("Открыть"), "open"), (Localization.Text("Добавить в проект…"), "link"), (Localization.Text("Переместить в папку…"), "move"), (Localization.Text("Изменить теги…"), "tags"), (Localization.Text("Независимая копия"), "copy"), (Localization.Text("Убрать из проекта"), "unlink"), (Localization.Text("Удалить из библиотеки…"), "delete") })
        {
            var item = new MenuItem { Header = pair.Item1, Tag = pair.Item2 };
            item.Click += (_, _) => RunMenuAction(pair.Item2);
            context.Items.Add(item);
        }
        assetList.ContextMenu = context;
        var createUnreal = new MenuItem { Header = Localization.Text("Создать текстуру в Unreal…"), Tag = "create-unreal" };
        createUnreal.Click += (_, _) => RunMenuAction("create-unreal");
        context.Items.Insert(1, createUnreal);
        assetList.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_deferredClick is not { } asset) return;
            _deferredClick = null; assetList.ReleaseMouseCapture(); assetList.SelectedItems.Clear(); assetList.SelectedItem = asset;
        };
    }

    public void CreateProject() => NewProjectClicked(this, new());

    public void Bind(WorkspaceCatalog catalog)
    {
        _catalog = catalog;
        var state = catalog.State;
        searchInput.Text = state.Search; scopeInput.SelectedIndex = state.Scope;
        kindInput.SelectedIndex = state.Kind; filterWidth.Text = state.Width; filterHeight.Text = state.Height; alphaInput.SelectedIndex = state.Transparency;
        state.AssetView = Math.Clamp(state.AssetView, 0, 2);
        assetViewInput.SelectedIndex = state.AssetView;
        _ready = true; RebuildTree(); Refresh();
    }

    public void Navigate(string view, Guid? project = null, Guid? folder = null)
    {
        if (_catalog is null) return;
        var state = _catalog.State; state.View = view; state.ProjectId = project; state.FolderId = folder;
        if (view != "project") { _building = true; foreach (var item in projectTree.Items.OfType<TreeViewItem>()) item.IsSelected = false; _building = false; }
        Refresh();
    }

    public void Refresh(Guid? select = null)
    {
        if (!_ready || _catalog is null) return;
        var state = _catalog.State;
        if (state.ProjectId is { } id && !_catalog.State.Projects.Any(p => p.Id == id)) { state.View = "all"; state.ProjectId = state.FolderId = null; }
        var selected = Selection.Select(a => a.Id).ToHashSet();
        var assets = _catalog.Query(state.View, state.ProjectId, state.FolderId, scopeInput.SelectedIndex, searchInput.Text, kindInput.SelectedIndex, filterWidth.Text, filterHeight.Text, alphaInput.SelectedIndex);
        int view = Math.Clamp(state.AssetView, 0, 2);
        string[] panels = ["TileItemsPanel", "ListItemsPanel", "SmallTileItemsPanel"];
        string[] templates = ["AssetTileTemplate", "AssetListTemplate", "AssetSmallTileTemplate"];
        string[] styles = ["TileAssetItem", "ListAssetItem", "SmallTileAssetItem"];
        var panel = (ItemsPanelTemplate)Resources[panels[view]];
        if (assetList.ItemsPanel != panel) assetList.ItemsPanel = panel;
        assetList.ItemTemplate = (DataTemplate)Resources[templates[view]];
        assetList.ItemContainerStyle = (Style)Resources[styles[view]];
        assetList.ItemsSource = assets;
        foreach (var asset in assets.Where(a => select == a.Id || select is null && selected.Contains(a.Id))) assetList.SelectedItems.Add(asset);
        if (assetList.SelectedItem is null && state.SelectedAsset is { } last) assetList.SelectedItem = assets.FirstOrDefault(a => a.Id == last);
        viewTitle.Text = state.View switch { "unassigned" => Localization.Text("Без проекта"), "recent" => Localization.Text("Недавние"), "project" => state.ProjectId is { } project ? _catalog.Location(new WorkspaceLink { ProjectId = project, FolderId = state.FolderId }) : Localization.Text("Проект"), _ => Localization.Text("Все ассеты") };
        emptyHint.Visibility = assets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        countLabel.Text = Localization.Format("Материалов: {0} · Выбрано: {1} · Ctrl / Shift — несколько · Enter — открыть · Правая кнопка — действия", assets.Count, Selection.Count);
        SaveFilters(); UpdateDetails();
    }

    public void RebuildTree()
    {
        if (_catalog is null) return;
        _building = true; projectTree.Items.Clear();
        foreach (var project in _catalog.State.Projects)
        {
            var node = MakeNode(project.Name, project, project.Id == _catalog.State.ProjectId && _catalog.State.FolderId is null);
            var folders = _catalog.State.Folders.Where(f => f.ProjectId == project.Id).ToLookup(f => f.ParentId);
            AddFolders(node, folders, null, []); projectTree.Items.Add(node);
        }
        _building = false;
    }
    private TreeViewItem MakeNode(string name, object tag, bool selected)
    {
        var node = new TreeViewItem
        {
            Header = new TextBlock { Text = name, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis },
            Tag = tag, IsExpanded = true, IsSelected = selected, ToolTip = name, Padding = new Thickness(2, 0, 2, 0)
        };
        AutomationProperties.SetName(node, name);
        Guid project = tag is WorkspaceProject p ? p.Id : ((WorkspaceFolder)tag).ProjectId;
        Guid? folder = (tag as WorkspaceFolder)?.Id;
        if (_catalog!.IsUnrealProject(project))
        {
            if (tag is WorkspaceProject)
            {
                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var icon = new System.Windows.Shapes.Path
                {
                    Data = UnrealLogo.Value, Width = 18, Height = 18, Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 6, 0), ToolTip = Localization.Text("Проект Unreal Engine")
                };
                icon.SetBinding(System.Windows.Shapes.Path.FillProperty, new Binding(nameof(Foreground)) { Source = node });
                header.Children.Add(icon);
                var title = new TextBlock { Text = name, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(title, 1);
                header.Children.Add(title);
                node.Header = header;
            }
            return node;
        }
        var menu = new ContextMenu();
        var create = new MenuItem { Header = Localization.Text("Создать папку…") };
        create.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() => Safe(() => CreateFolder(project, folder))));
        var rename = new MenuItem { Header = Localization.Text("Переименовать…") };
        rename.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() => Safe(() => RenameSection(tag))));
        var delete = new MenuItem { Header = folder is null ? Localization.Text("Удалить проект…") : Localization.Text("Удалить папку…") };
        delete.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() => Safe(() => DeleteSection(project, folder))));
        menu.Items.Add(create); menu.Items.Add(rename); menu.Items.Add(new Separator()); menu.Items.Add(delete);
        menu.Opened += (_, _) => node.IsSelected = true;
        node.ContextMenu = menu;
        node.PreviewMouseRightButtonDown += (_, e) =>
        {
            // A nested folder also routes through its ancestors. Select only the actual target.
            if (FindTreeItem(e.OriginalSource as DependencyObject) != node) return;
            node.IsSelected = true; node.Focus();
        };
        return node;
    }
    private void AddFolders(TreeViewItem parent, ILookup<Guid?, WorkspaceFolder> folders, Guid? parentId, HashSet<Guid> visited)
    {
        foreach (var folder in folders[parentId].OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!visited.Add(folder.Id)) continue;
            var item = MakeNode(folder.Name, folder, folder.Id == _catalog!.State.FolderId);
            parent.Items.Add(item); AddFolders(item, folders, folder.Id, visited);
        }
    }

    private static readonly Lazy<Geometry> UnrealLogo = new(() =>
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/DomainColorTest;component/Assets/unreal-mark.svg"))!;
        using var stream = resource.Stream;
        var svg = XDocument.Load(stream);
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        foreach (var path in svg.Descendants().Where(e => e.Name.LocalName == "path"))
            geometry.Children.Add(Geometry.Parse("F0 " + path.Attribute("d")!.Value));
        geometry.Freeze();
        return geometry;
    });

    private void ConnectUnrealClicked(object sender, RoutedEventArgs e) => ConnectUnrealRequested?.Invoke();

    internal void ShowUnrealStatus(string? status)
    {
        unrealStatus.Text = status ?? "";
        unrealStatus.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;
    }

    internal IEnumerable<WorkspaceAsset> VisibleUnrealAssets()
    {
        if (!IsVisible) yield break;
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(assetList);
        while (queue.TryDequeue(out var element))
        {
            if (element is ListBoxItem item)
            {
                if (item.Content is WorkspaceAsset { Unreal: not null } asset && item.IsVisible &&
                    item.TransformToAncestor(assetList).TransformBounds(new Rect(item.RenderSize))
                        .IntersectsWith(new Rect(assetList.RenderSize))) yield return asset;
                continue;
            }
            for (int q = 0; q < VisualTreeHelper.GetChildrenCount(element); q++)
                queue.Enqueue(VisualTreeHelper.GetChild(element, q));
        }
    }

    private void SaveFilters()
    {
        if (_catalog is null) return;
        var state = _catalog.State; state.Search = searchInput.Text; state.Scope = scopeInput.SelectedIndex; state.Kind = kindInput.SelectedIndex;
        state.Width = filterWidth.Text; state.Height = filterHeight.Text; state.Transparency = alphaInput.SelectedIndex;
        _catalog.Save();
    }
    private void FiltersChanged(object sender, RoutedEventArgs e) { if (_ready) Safe(() => Refresh()); }
    private void AssetViewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _catalog is null || assetViewInput.SelectedIndex < 0) return;
        _catalog.State.AssetView = assetViewInput.SelectedIndex;
        Safe(() => Refresh());
    }
    private void ResetFilters(object sender, RoutedEventArgs e)
    {
        _ready = false; searchInput.Clear(); filterWidth.Clear(); filterHeight.Clear(); kindInput.SelectedIndex = alphaInput.SelectedIndex = scopeInput.SelectedIndex = 0; _ready = true; Refresh();
    }
    private void AllClicked(object sender, RoutedEventArgs e) => Safe(() => Navigate("all"));
    private void UnassignedClicked(object sender, RoutedEventArgs e) => Safe(() => Navigate("unassigned"));
    private void RecentClicked(object sender, RoutedEventArgs e) => Safe(() => Navigate("recent"));
    private void TreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (!_ready || _building || e.NewValue is not TreeViewItem node) return;
        Safe(() => { if (node.Tag is WorkspaceProject project) Navigate("project", project.Id); else if (node.Tag is WorkspaceFolder folder) Navigate("project", folder.ProjectId, folder.Id); });
    }
    private void NewProjectClicked(object sender, RoutedEventArgs e) => Safe(() =>
    {
        string? name = Prompt(Localization.Text("Создать проект"), Localization.Text("Название проекта")); if (name is null) return;
        var project = _catalog!.CreateProject(name); Navigate("project", project.Id); RebuildTree();
    });
    private void CreateFolder(Guid project, Guid? parent)
    {
        string? name = Prompt(Localization.Text("Создать папку"), Localization.Text("Название папки")); if (name is null) return;
        var folder = _catalog!.CreateFolder(project, parent, name); Navigate("project", project, folder.Id); RebuildTree();
    }
    private void RenameSection(object section)
    {
        Guid id = section is WorkspaceProject project ? project.Id : ((WorkspaceFolder)section).Id;
        string currentName = section is WorkspaceProject p ? p.Name : ((WorkspaceFolder)section).Name;
        string? name = Prompt(Localization.Text("Переименовать"), Localization.Text("Название"), currentName); if (name is null) return;
        _catalog!.Rename(id, name); RebuildTree(); Refresh();
    }
    private void DeleteSection(Guid project, Guid? folder)
    {
        if (TesseraDialog.Show(Window.GetWindow(this), Localization.Text("Удалить выбранный раздел? Ассеты останутся в библиотеке. Содержимое папки перейдёт в родительскую."), Localization.Text("Структура проекта"), MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (folder is { } id)
        {
            var parent = _catalog!.State.Folders.Single(f => f.Id == id).ParentId;
            _catalog.RemoveFolder(id); Navigate("project", project, parent);
        }
        else { _catalog!.RemoveProject(project); Navigate("all"); }
        RebuildTree();
    }

    private void AssetSelected(object sender, SelectionChangedEventArgs e) { if (_ready) UpdateDetails(); }
    private bool CanManageSelection => _catalog?.IsUnrealProject(_catalog.State.ProjectId) != true && Selection.All(a => a.Unreal is null);
    private void UpdateDetails()
    {
        var selection = Selection; var asset = selection.FirstOrDefault();
        var managementVisibility = CanManageSelection ? Visibility.Visible : Visibility.Collapsed;
        assetTagsPanel.Visibility = assetManagementPanel.Visibility = managementVisibility;
        foreach (var item in assetList.ContextMenu.Items.OfType<MenuItem>())
            item.Visibility = item.Tag switch
            {
                "open" => Visibility.Visible,
                "create-unreal" => selection.Count == 1 && asset?.Kind == WorkspaceAssetKind.Image &&
                    _catalog!.State.Projects.Any(p => p.IsUnreal) ? Visibility.Visible : Visibility.Collapsed,
                _ => managementVisibility
            };
        openAssetButton.IsEnabled = asset is not null;
        detailName.Text = selection.Count > 1 ? Localization.Format("Выбрано: {0}", selection.Count) : asset?.Name ?? Localization.Text("Выберите ассет");
        detailSummary.Text = asset?.Summary ?? ""; detailPreview.Source = asset?.Thumbnail;
        tagsInput.Text = asset is null ? "" : string.Join(", ", asset.Tags.Where(t => selection.All(a => a.Tags.Contains(t))));
        usageLabel.Text = asset is null ? "" : Localization.Text("Используется:\n") + (string.IsNullOrEmpty(asset.LocationLabel) ? Localization.Text("Без проекта") :
            string.Join("\n", asset.LocationLabel.Split(" · ").Select(location => location + " / " + asset.Name)));
        if (asset is not null && _catalog is not null) _catalog.State.SelectedAsset = asset.Id;
        if (_ready && _catalog is not null) countLabel.Text = Localization.Format("Материалов: {0} · Выбрано: {1} · Ctrl / Shift — несколько · Enter — открыть · Правая кнопка — действия", assetList.Items.Count, selection.Count);
    }
    private void OpenClicked(object sender, RoutedEventArgs e) { if (Selection.FirstOrDefault() is { } asset) OpenRequested?.Invoke(asset); }
    private void OpenDoubleClick(object sender, MouseButtonEventArgs e) { if (ItemsControl.ContainerFromElement(assetList, e.OriginalSource as DependencyObject) is ListBoxItem) OpenClicked(sender, e); }
    private void AssetKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { OpenClicked(sender, e); e.Handled = true; } }
    private void TagsClicked(object sender, RoutedEventArgs e) => Safe(() => SetTags(tagsInput.Text));
    private void SetTags(string text)
    {
        if (!CanManageSelection) return;
        var tags = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var asset in Selection) asset.Tags = [.. tags];
        _catalog!.Save(); Refresh();
    }
    private void AddToProjectClicked(object sender, RoutedEventArgs e) => Safe(() => ChooseDestination(false));
    private void MoveClicked(object sender, RoutedEventArgs e) => Safe(() => ChooseDestination(true));
    private void ChooseDestination(bool currentProject)
    {
        if (!CanManageSelection) return;
        if (Selection.Count == 0) return;
        var state = _catalog!.State;
        if (currentProject && state.ProjectId is null) { Tell(Localization.Text("Сначала выберите проект.")); return; }
        var choices = new List<(string Name, Guid Project, Guid? Folder)>();
        foreach (var project in state.Projects.Where(p => !p.IsUnreal && (!currentProject || p.Id == state.ProjectId)))
        {
            choices.Add((project.Name, project.Id, null));
            foreach (var folder in state.Folders.Where(f => f.ProjectId == project.Id)) choices.Add((_catalog.Location(new WorkspaceLink { ProjectId = project.Id, FolderId = folder.Id }), project.Id, folder.Id));
        }
        if (choices.Count == 0) { Tell(Localization.Text("Создайте проект кнопкой + слева.")); return; }
        int choice = Choose(Localization.Text("Выберите папку проекта"), choices.Select(c => c.Name).ToArray()); if (choice < 0) return;
        var target = choices[choice]; foreach (var asset in Selection) _catalog.Link(asset.Id, target.Project, target.Folder);
        Refresh(); Tell(Localization.Text("Материалы добавлены: ") + target.Name);
    }
    private void UnlinkClicked(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (!CanManageSelection) return;
        if (_catalog!.State.ProjectId is not { } project) { Tell(Localization.Text("Сначала выберите проект.")); return; }
        if (_catalog.IsUnrealProject(project)) return;
        foreach (var asset in Selection) _catalog.Unlink(asset.Id, project); Refresh();
    });
    private void CopyClicked(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (!CanManageSelection) return;
        if (_catalog!.State.ProjectId is null) { Tell(Localization.Text("Выберите проект, для которого нужна независимая копия.")); return; }
        foreach (var asset in Selection.ToArray()) ActionRequested?.Invoke("copy", asset); Refresh();
    });
    private void DeleteClicked(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (!CanManageSelection) return;
        var assets = Selection.ToArray(); if (assets.Length == 0) return;
        if (TesseraDialog.Show(Window.GetWindow(this), Localization.Format("Удалить из библиотеки: {0}? Они исчезнут из всех проектов. Внешние исходные файлы сохранятся.\n\n", assets.Length) + string.Join("\n", assets.Select(a => a.Name + " · " + a.LocationLabel)), Localization.Text("Удалить ассеты"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        foreach (var asset in assets) ActionRequested?.Invoke("delete", asset); Refresh();
    });
    private void RunMenuAction(string action)
    {
        if (action is not ("open" or "create-unreal") && !CanManageSelection) return;
        switch (action)
        {
            case "create-unreal":
                if (Selection is [var asset] && asset.Kind == WorkspaceAssetKind.Image)
                    ActionRequested?.Invoke(action, asset);
                break;
            case "open": OpenClicked(this, new()); break; case "link": AddToProjectClicked(this, new()); break;
            case "move": MoveClicked(this, new()); break; case "copy": CopyClicked(this, new()); break;
            case "unlink": UnlinkClicked(this, new()); break; case "delete": DeleteClicked(this, new()); break;
            case "tags": string? tags = Prompt(Localization.Text("Общие теги выбранных ассетов"), Localization.Text("Теги через запятую (заменяют текущие)"), tagsInput.Text); if (tags is not null) Safe(() => SetTags(tags)); break;
        }
    }

    private void AssetMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this); _deferredClick = null;
        if (e.ClickCount > 1 || Keyboard.Modifiers != ModifierKeys.None || Selection.Count <= 1) return;
        if (ItemsControl.ContainerFromElement(assetList, e.OriginalSource as DependencyObject) is ListBoxItem { IsSelected: true, Content: WorkspaceAsset asset })
        {
            // Keep the group while a drag starts. A plain click collapses it on mouse-up.
            _deferredClick = asset; assetList.Focus(); Mouse.Capture(assetList, CaptureMode.SubTree); e.Handled = true;
        }
    }
    private void AssetMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(this);
        if (e.LeftButton != MouseButtonState.Pressed || Selection.Count == 0 || Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _deferredClick = null;
        assetList.ReleaseMouseCapture();
        DragDrop.DoDragDrop(assetList, new DataObject(AssetDragFormat, Selection.Select(a => a.Id).ToArray()), DragDropEffects.Copy | DragDropEffects.Move);
    }
    private void TreeMouseDown(object sender, MouseButtonEventArgs e) { _dragStart = e.GetPosition(this); _treeDrag = FindTreeItem(e.OriginalSource as DependencyObject)?.Tag; }
    private void TreeMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(this);
        if (_treeDrag is not WorkspaceFolder { UnrealPath: null } folder || e.LeftButton != MouseButtonState.Pressed || Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _treeDrag = null; DragDrop.DoDragDrop(projectTree, new DataObject(FolderDragFormat, folder.Id), DragDropEffects.Move);
    }
    private void TreeDragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(AssetDragFormat) || e.Data.GetDataPresent(FolderDragFormat) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; }
    private void TreeDrop(object sender, DragEventArgs e) => Safe(() =>
    {
        var target = FindTreeItem(e.OriginalSource as DependencyObject)?.Tag;
        Guid? project = target is WorkspaceProject p ? p.Id : (target as WorkspaceFolder)?.ProjectId;
        Guid? folder = (target as WorkspaceFolder)?.Id; if (project is null) return;
        if (_catalog!.IsUnrealProject(project)) { e.Handled = true; return; }
        if (e.Data.GetData(AssetDragFormat) is Guid[] assets) foreach (Guid asset in assets) _catalog!.Link(asset, project.Value, folder);
        if (e.Data.GetData(FolderDragFormat) is Guid moved)
        {
            if (_catalog!.State.Folders.Single(f => f.Id == moved).ProjectId != project) throw new ArgumentException(Localization.Text("Папки перемещаются внутри своего проекта."));
            _catalog.MoveFolder(moved, folder);
        }
        e.Handled = true; RebuildTree(); Refresh();
    });
    private static TreeViewItem? FindTreeItem(DependencyObject? item)
    {
        while (item is not null && item is not TreeViewItem) item = item is FrameworkContentElement content ? content.Parent : System.Windows.Media.VisualTreeHelper.GetParent(item);
        return item as TreeViewItem;
    }

    private void Safe(Action action) { try { action(); } catch (Exception ex) { Tell(ex.Message); TesseraDialog.Show(Window.GetWindow(this), ex.Message, "Tessera", MessageBoxButton.OK, MessageBoxImage.Warning); } }
    private void Tell(string text) => StatusChanged?.Invoke(text);

    public string? Prompt(string title, string label, string value = "")
    {
        var text = new TextBox { Text = value, Margin = new Thickness(0, 8, 0, 16), MinWidth = 300 };
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }); panel.Children.Add(text);
        return InlineDialog(title, panel, text, Localization.Text("Готово")) ? text.Text : null;
    }
    internal int Choose(string title, string[] choices)
    {
        var panel = new StackPanel(); var combo = new ComboBox { ItemsSource = choices, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 16), MinWidth = 300 }; panel.Children.Add(combo);
        return InlineDialog(title, panel, combo, Localization.Text("Добавить сюда")) ? combo.SelectedIndex : -1;
    }
    public string? ChooseBoard(IReadOnlyList<WorkspaceAsset> boards)
    {
        if (boards.Count == 0) { Tell(Localization.Text("Сначала создайте доску через «Файл → Создать → Доску».")); return null; }
        int choice = Choose(Localization.Text("Добавить изображение на доску"), boards.Select(b => b.Name).ToArray());
        return choice < 0 ? null : boards[choice].Id.ToString();
    }
    private bool InlineDialog(string title, StackPanel content, Control input, string action)
    {
        var previous = browserGrid.Children.Cast<UIElement>().Select(e => (Element: e, Enabled: e.IsEnabled)).ToArray();
        foreach (var item in previous) item.Element.IsEnabled = false;
        var overlay = new Grid { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0, 0, 0)) };
        Grid.SetColumnSpan(overlay, 3); Panel.SetZIndex(overlay, 20);
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 20), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(content);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = Localization.Text("Отмена"), Margin = new Thickness(0, 0, 8, 0) };
        var okay = new Button { Content = action };
        buttons.Children.Add(cancel); buttons.Children.Add(okay); panel.Children.Add(buttons);
        var border = new Border { Child = panel, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 540, BorderThickness = new Thickness(1), Margin = new Thickness(16) };
        border.SetResourceReference(Border.BackgroundProperty, "Ts.Panel"); border.SetResourceReference(Border.BorderBrushProperty, "Ts.Border");
        overlay.Children.Add(border); KeyboardNavigation.SetTabNavigation(overlay, KeyboardNavigationMode.Cycle);
        var frame = new System.Windows.Threading.DispatcherFrame(); bool accepted = false;
        void Finish(bool success) { accepted = success; frame.Continue = false; }
        okay.Click += (_, _) => Finish(true); cancel.Click += (_, _) => Finish(false);
        overlay.PreviewKeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Escape) { Finish(e.Key == Key.Enter); e.Handled = true; } };
        overlay.Loaded += (_, _) => { input.Focus(); if (input is TextBox text) text.SelectAll(); };
        browserGrid.Children.Add(overlay);
        try { System.Windows.Threading.Dispatcher.PushFrame(frame); }
        finally { browserGrid.Children.Remove(overlay); foreach (var item in previous) item.Element.IsEnabled = item.Enabled; }
        return accepted;
    }
}
