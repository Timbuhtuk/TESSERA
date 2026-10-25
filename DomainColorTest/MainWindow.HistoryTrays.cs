using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace DomainColorTest;

public sealed class HistoryTrayState
{
    public int Version { get; set; } = 1;
    public double Height { get; set; } = 120;
    public double ExpandedHeight { get; set; } = 290;
    public double WidthRatio { get; set; } = .5;
    public Dictionary<Guid, BranchPresentation> Branches { get; set; } = [];
    public Dictionary<string, Point> Scroll { get; set; } = [];
    public Dictionary<Guid, Guid?> CurrentFolders { get; set; } = [];
    public HashSet<Guid> CollapsedFolders { get; set; } = [];
}

public partial class MainWindow
{
    private HistoryTrayState _trayState = new();
    private readonly ObservableCollection<object> _traySources = [];
    private HistoryBranchLayout? _branchLayout;
    private SourceEntry? _trayHistorySource;
    private readonly DispatcherTimer _traySaveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool _traysReady, _refreshingTrays, _traysExpanded;
    private Guid? _sourceTrayFolder;
    private string _sourceScrollKey = "";
    private readonly Dictionary<VirtualizingTray, int> _pendingTrayRestores = [];
    private int _trayRestoreRevision;
    private string TrayStatePath => Path.Combine(_library.DirectoryPath, "editor-history.json");

    private void InitializeHistoryTrays()
    {
        try
        {
            if (File.Exists(TrayStatePath))
            {
                var saved = JsonSerializer.Deserialize<HistoryTrayState>(File.ReadAllText(TrayStatePath));
                if (saved?.Version == 1) _trayState = saved;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { statusLabel.Text = Localization.Text("Не удалось восстановить расположение лент: ") + ex.Message; }
        double ratio = double.IsFinite(_trayState.WidthRatio) ? Math.Clamp(_trayState.WidthRatio, .15, .85) : .5;
        historyRow.ColumnDefinitions[0].Width = new GridLength(ratio, GridUnitType.Star);
        historyRow.ColumnDefinitions[2].Width = new GridLength(1 - ratio, GridUnitType.Star);
        _trayState.Height = double.IsFinite(_trayState.Height) ? Math.Max(120, _trayState.Height) : 120;
        _trayState.ExpandedHeight = double.IsFinite(_trayState.ExpandedHeight) ? Math.Max(160, _trayState.ExpandedHeight) : 290;
        trayHeight.Height = new GridLength(_trayState.Height);
        _traysExpanded = _trayState.Height > 150;
        _traySaveTimer.Tick += (_, _) => { _traySaveTimer.Stop(); SaveTrayState(); };
        trayHeightSplitter.DragCompleted += (_, _) => CommitTrayHeight();
        trayWidthSplitter.DragCompleted += (_, _) =>
        {
            double width = historyRow.ColumnDefinitions[0].ActualWidth + historyRow.ColumnDefinitions[2].ActualWidth;
            if (width > 0) _trayState.WidthRatio = historyRow.ColumnDefinitions[0].ActualWidth / width;
            ScheduleTraySave();
        };
        trayHeightSplitter.PreviewMouseDoubleClick += (_, e) =>
        {
            double target = _traysExpanded ? 120 : _trayState.ExpandedHeight;
            trayHeight.Height = new GridLength(Math.Min(target, MaximumTrayHeight()));
            CommitTrayHeight(); e.Handled = true;
        };
        previewArea.SizeChanged += (_, _) =>
        {
            if (!previewArea.IsVisible || previewArea.ActualHeight <= 246) return;
            double maximum = MaximumTrayHeight();
            if (trayHeight.Height.Value > maximum) { trayHeight.Height = new GridLength(maximum); CommitTrayHeight(); }
        };
        generationStrip.ScrollPositionChanged += (_, _) => { RememberTrayScroll(); ScheduleTraySave(); };
        sourceStrip.ScrollPositionChanged += (_, _) => { RememberTrayScroll(); ScheduleTraySave(); };
        _sources.CollectionChanged += (_, _) => RefreshSourceTray();
        _workspaceSources.CollectionChanged += (_, _) => RefreshSourceTray();
        generationStrip.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (_processing) return;
            if (ItemsControl.ContainerFromElement(generationStrip, e.OriginalSource as DependencyObject) is not ListBoxItem { Content: GenerationEntry generation } card) return;
            card.IsSelected = true;
            OpenHistoryContextMenu(generation, card); e.Handled = true;
        };
        generationStrip.PreviewKeyDown += (_, e) =>
        {
            if (_processing || generationStrip.SelectedItem is not GenerationEntry selected) return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Apps || key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift)
            { OpenHistoryContextMenu(selected, generationStrip); e.Handled = true; }
        };
        generationStrip.DragLeave += (_, _) => generationStrip.InsertionY = null;
        Closing += (_, e) => { if (!e.Cancel) { RememberTrayScroll(); SaveTrayState(); } };
        Loaded += (_, _) =>
        {
            if (!previewArea.IsVisible || previewArea.ActualHeight <= 246) return;
            trayHeight.Height = new GridLength(Math.Min(_trayState.Height, MaximumTrayHeight()));
            CommitTrayHeight(); RestoreTrayScroll();
        };
        _traysReady = true;
    }

    private double MaximumTrayHeight() => Math.Max(120, previewArea.ActualHeight - 246);
    private void CommitTrayHeight()
    {
        RememberTrayScroll();
        double height = Math.Clamp(trayHeight.ActualHeight, 120, MaximumTrayHeight());
        // DragCompleted reports the updated row definition even before the next layout pass.
        if (trayHeight.Height.IsAbsolute) height = Math.Clamp(trayHeight.Height.Value, 120, MaximumTrayHeight());
        _trayState.Height = height;
        if (height > 150) _trayState.ExpandedHeight = height;
        bool expanded = height > 150;
        if (expanded != _traysExpanded) { _traysExpanded = expanded; RefreshSourceTray(); RestoreTrayScroll(); }
        ScheduleTraySave();
    }

    private BranchPresentation CurrentBranchPresentation()
    {
        if (_trayHistorySource is null) return new();
        if (!_trayState.Branches.TryGetValue(_trayHistorySource.Id, out var presentation))
            _trayState.Branches[_trayHistorySource.Id] = presentation = new();
        return presentation;
    }

    private void ConfigureHistorySource(SourceEntry source)
    {
        RememberTrayScroll();
        if (_trayHistorySource is not null) _trayHistorySource.Generations.CollectionChanged -= HistoryChanged;
        _trayHistorySource = source;
        source.Generations.CollectionChanged += HistoryChanged;
        RefreshHistoryLayout();
        ChooseSourceFolder(source);
        RestoreTrayScroll();
    }

    private void HistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A new result must win over a deferred selection/scroll restoration from opening its source.
        _pendingTrayRestores.Remove(generationStrip);
        RefreshHistoryLayout();
    }

    private void RefreshHistoryLayout()
    {
        if (_trayHistorySource is null) { generationStrip.SetLayout([]); return; }
        _branchLayout = HistoryBranchLayout.Build(_trayHistorySource.Generations.ToArray(), CurrentBranchPresentation());
        var bounds = _trayHistorySource.Generations.Select(g => _branchLayout.Bounds[g.Id]).ToArray();
        var lines = _branchLayout.Connections.Select(link =>
        {
            var parent = _branchLayout.Bounds[link.Parent]; var child = _branchLayout.Bounds[link.Child];
            return (new Point(parent.Right, parent.Y + parent.Height / 2), new Point(child.Left, child.Y + child.Height / 2));
        }).ToArray();
        generationStrip.SetLayout(bounds, lines);
    }

    private void PromoteHistoryBranch(GenerationEntry entry, int? insertion = null)
    {
        if (_branchLayout is null || _trayHistorySource?.Generations.Contains(entry) != true) return;
        _branchLayout.Promote(entry.Id, CurrentBranchPresentation(), insertion);
        RefreshHistoryLayout(); generationStrip.ScrollIntoView(entry); ScheduleTraySave();
    }

    private void OpenHistoryContextMenu(GenerationEntry generation, FrameworkElement target)
    {
        var menu = new ContextMenu();
        menu.Resources.MergedDictionaries.Add(Resources);
        var detach = new MenuItem { Header = Localization.Text("Вынести в отдельную ветку") };
        detach.IsEnabled = _branchLayout?.DisplayParents.GetValueOrDefault(generation.Id) is not null;
        detach.Click += (_, _) => PromoteHistoryBranch(generation);
        var macro = new MenuItem { Header = Localization.Text("Создать макрос…") };
        macro.Click += (_, _) => CreateMacro(generation);
        menu.Items.Add(detach); menu.Items.Add(macro);
        menu.PlacementTarget = target; menu.IsOpen = true;
    }

    private bool IsHistoryDropTarget(DependencyObject? origin)
    {
        for (var current = origin; current is not null; current = ParentOf(current)) if (ReferenceEquals(current, generationStrip)) return true;
        return false;
    }
    private bool HistoryDrop(DragEventArgs e, bool commit)
    {
        generationStrip.InsertionY = null;
        if (!e.Data.GetDataPresent(GenerationDragFormat) || !IsHistoryDropTarget(e.OriginalSource as DependencyObject)) return false;
        bool supported = TryGetDraggedGeneration(e.Data, out var source, out var generation);
        bool valid = !_processing && supported && source == _activeSource;
        int insertion = 0;
        if (valid && _branchLayout is { } layout)
        {
            double y = e.GetPosition(generationStrip).Y + generationStrip.ScrollPosition.Y;
            double boundary = 0;
            var rootBounds = layout.RootBounds;
            foreach (var root in layout.Roots.Where(id => id != generation.Id))
            {
                var bounds = rootBounds[root];
                if (y >= bounds.Top && y <= bounds.Bottom) { valid = false; break; }
                if (y > bounds.Bottom) { insertion++; boundary = bounds.Bottom + 30; }
                else { boundary = Math.Min(boundary == 0 ? bounds.Top - 3 : boundary, bounds.Top - 3); break; }
            }
            if (valid) generationStrip.InsertionY = boundary;
            var point = e.GetPosition(generationStrip);
            if (!commit && point.Y < 18) generationStrip.Panel?.LineUp();
            else if (!commit && point.Y > generationStrip.ActualHeight - 18) generationStrip.Panel?.LineDown();
        }
        e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        if (commit && valid && _branchLayout is not null && TryGetDraggedGeneration(e.Data, out _, out var moved))
        {
            PromoteHistoryBranch(moved, insertion);
        }
        if (commit) generationStrip.InsertionY = null;
        return true;
    }

    private void ChooseSourceFolder(SourceEntry source)
    {
        if (_workspaceSourceProject is { } project && _workspaceCatalog is { } catalog)
        {
            _sourceTrayFolder = catalog.State.Links.FirstOrDefault(l => l.ProjectId == project && l.AssetId == source.Id)?.FolderId;
            _trayState.CurrentFolders[project] = _sourceTrayFolder;
            if (catalog.State.View == "project" && catalog.State.ProjectId == project) catalog.State.FolderId = _sourceTrayFolder;
            var seen = new HashSet<Guid>(); Guid? folder = _sourceTrayFolder;
            while (folder is { } id && seen.Add(id))
            {
                _trayState.CollapsedFolders.Remove(id);
                folder = catalog.State.Folders.FirstOrDefault(f => f.ProjectId == project && f.Id == id)?.ParentId;
            }
        }
        else _sourceTrayFolder = null;
        RefreshSourceTray();
    }

    private void NavigateSourceFolder(Guid? folder, bool toggle = false)
    {
        RememberTrayScroll();
        _sourceTrayFolder = folder;
        if (_workspaceSourceProject is { } project)
        {
            _trayState.CurrentFolders[project] = folder;
            if (_workspaceCatalog?.State is { View: "project" } state && state.ProjectId == project) state.FolderId = folder;
        }
        if (toggle && folder is { } id)
        { if (!_trayState.CollapsedFolders.Add(id)) _trayState.CollapsedFolders.Remove(id); }
        RefreshSourceTray(); RestoreTrayScroll(); ScheduleTraySave();
    }

    private void TrayFolderClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TrayFolderHeader header) header.Activate?.Invoke();
        e.Handled = true;
    }

    private void RefreshSourceTray()
    {
        if (!_traysReady || _refreshingTrays) return;
        _refreshingTrays = true;
        bool restoring = _restoringHistory; _restoringHistory = true;
        try
        {
            RememberTrayScroll();
            var items = new List<object>(); var positions = new List<Rect>();
            var sources = (_workspaceReady ? _workspaceSources : _sources).ToArray();
            var state = _workspaceCatalog?.State;
            var project = state?.Projects.FirstOrDefault(p => p.Id == _workspaceSourceProject);
            var links = state?.Links.Where(l => l.ProjectId == project?.Id).ToDictionary(l => l.AssetId);
            void AddImages(IEnumerable<SourceEntry> images, double y, int depth)
            {
                int col = 0;
                foreach (var source in images)
                { items.Add(source); positions.Add(new Rect(col++ * 130 + 4 + depth * 16, y, 124, 68)); }
            }
            IEnumerable<SourceEntry> InFolder(Guid? folder) => sources.Where(s => links?.GetValueOrDefault(s.Id)?.FolderId == folder);
            if (project is null)
            {
                _sourceTrayFolder = null; AddImages(sources, 4, 0);
                sourceCaption.Text = Localization.Text(_workspaceSourcesUnassigned ? "Без проекта" : "ИСТОЧНИКИ");
            }
            else
            {
                if (_sourceTrayFolder is { } current && !state!.Folders.Any(f => f.Id == current && f.ProjectId == project.Id)) _sourceTrayFolder = null;
                BuildSourceBreadcrumb(project);
                if (!_traysExpanded) AddImages(InFolder(_sourceTrayFolder), 4, 0);
                else
                {
                    double y = 0;
                    var seen = new HashSet<Guid>();
                    var pending = new Stack<(Guid? Id, string Name, int Depth)>();
                    pending.Push((null, project.Name, 0));
                    while (pending.TryPop(out var folder))
                    {
                        if (folder.Id is { } id && !seen.Add(id)) continue;
                        bool collapsed = folder.Id is { } fid && _trayState.CollapsedFolders.Contains(fid);
                        var selectedFolder = folder.Id;
                        items.Add(new TrayFolderHeader { FolderId = folder.Id, Label = (collapsed ? "▸ " : "▾ ") + folder.Name,
                            Activate = () => NavigateSourceFolder(selectedFolder, toggle: true) });
                        positions.Add(new Rect(4 + folder.Depth * 16, y, 280, 26)); y += 28;
                        if (collapsed) continue;
                        var images = InFolder(folder.Id).ToArray();
                        if (images.Length > 0) { AddImages(images, y, folder.Depth); y += 76; }
                        else
                        {
                            items.Add(new TrayFolderHeader { FolderId = folder.Id, Label = Localization.Text("Нет изображений"), IsEmpty = true });
                            positions.Add(new Rect(20 + folder.Depth * 16, y, 220, 28)); y += 32;
                        }
                        foreach (var child in state!.Folders.Where(f => f.ProjectId == project.Id && f.ParentId == folder.Id).OrderByDescending(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
                            pending.Push((child.Id, child.Name, folder.Depth + 1));
                    }
                }
            }
            if (project is not null && items.Count == 0)
            {
                items.Add(new TrayFolderHeader { Label = Localization.Text("Нет изображений"), IsEmpty = true });
                positions.Add(new Rect(4, 4, 220, 28));
            }
            string scope = _workspaceSourceProject?.ToString() ?? (_workspaceSourcesUnassigned ? "unassigned" : "all");
            string location = _traysExpanded ? "tree" : _sourceTrayFolder?.ToString() ?? "root";
            _sourceScrollKey = $"sources:{scope}:{location}";
            _traySources.Clear(); foreach (var item in items) _traySources.Add(item);
            sourceStrip.SetLayout(positions);
            if (!ReferenceEquals(sourceStrip.ItemsSource, _traySources)) sourceStrip.ItemsSource = _traySources;
            sourceStrip.SelectedItem = ProcessingBasis == 0 && items.Contains(_activeSource!) ? _activeSource : null;
            QueueTrayScrollRestore(sourceStrip, _sourceScrollKey);
        }
        finally { _restoringHistory = restoring; _refreshingTrays = false; }
    }

    private void BuildSourceBreadcrumb(WorkspaceProject project)
    {
        sourceCaption.Inlines.Clear();
        void Add(string name, Guid? folder)
        {
            if (sourceCaption.Inlines.Count > 0) sourceCaption.Inlines.Add(new Run(" › "));
            var link = new Hyperlink(new Run(name)); link.SetResourceReference(Hyperlink.ForegroundProperty, "Ts.TextSecondary");
            link.Click += (_, _) => NavigateSourceFolder(folder);
            sourceCaption.Inlines.Add(link);
        }
        Add(project.Name, null);
        var path = new Stack<WorkspaceFolder>(); var seen = new HashSet<Guid>(); Guid? id = _sourceTrayFolder;
        while (id is { } current && seen.Add(current) && _workspaceCatalog!.State.Folders.FirstOrDefault(f => f.Id == current && f.ProjectId == project.Id) is { } folder)
        { path.Push(folder); id = folder.ParentId; }
        sourceCaption.ToolTip = string.Join(" › ", new[] { project.Name }.Concat(path.Select(f => f.Name)));
        while (path.TryPop(out var folder)) Add(folder.Name, folder.Id);
    }

    private void RememberTrayScroll()
    {
        if (!_traysReady) return;
        if (_trayHistorySource is { } source && generationStrip.Panel is not null && !_pendingTrayRestores.ContainsKey(generationStrip))
            _trayState.Scroll["results:" + source.Id] = generationStrip.ScrollPosition;
        if (_sourceScrollKey.Length > 0 && sourceStrip.Panel is not null && !_pendingTrayRestores.ContainsKey(sourceStrip))
            _trayState.Scroll[_sourceScrollKey] = sourceStrip.ScrollPosition;
    }
    private void RestoreTrayScroll()
    {
        if (_trayHistorySource is { } source) QueueTrayScrollRestore(generationStrip, "results:" + source.Id);
        QueueTrayScrollRestore(sourceStrip, _sourceScrollKey);
    }
    private void QueueTrayScrollRestore(VirtualizingTray tray, string key)
    {
        if (key.Length == 0) return;
        var position = _trayState.Scroll.GetValueOrDefault(key);
        int revision = ++_trayRestoreRevision;
        _pendingTrayRestores[tray] = revision;
        // ListBox queues its own ScrollIntoView while restoring selection. Restore after it,
        // without letting that transient movement overwrite the saved position.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            if (_pendingTrayRestores.GetValueOrDefault(tray) != revision) return;
            tray.UpdateLayout(); tray.RestoreScroll(position);
            _pendingTrayRestores.Remove(tray);
            _trayState.Scroll[key] = tray.ScrollPosition;
        });
    }
    private void ScheduleTraySave() { if (!_traysReady) return; _traySaveTimer.Stop(); _traySaveTimer.Start(); }
    private void SaveTrayState()
    {
        try
        {
            Directory.CreateDirectory(_library.DirectoryPath);
            File.WriteAllText(TrayStatePath + ".tmp", JsonSerializer.Serialize(_trayState));
            File.Move(TrayStatePath + ".tmp", TrayStatePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { statusLabel.Text = Localization.Text("Не удалось сохранить расположение лент: ") + ex.Message; }
    }
}
