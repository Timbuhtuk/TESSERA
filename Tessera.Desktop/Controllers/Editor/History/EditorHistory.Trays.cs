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

namespace Tessera.Desktop;

internal sealed partial class EditorHistory
{
    private readonly HistoryTrayStore _trayStore;

    private HistoryTrayState _trayState = new();

    private bool _trayPreferencesDirty = true;

    private Guid? _loadedBranchSource;

    private BranchPresentation? _loadedBranch;

    private readonly Dictionary<string, Point> _pendingTrayScroll = [];

    private readonly ObservableCollection<object> _traySources = [];

    private readonly ObservableCollection<object> _historyItems = [];

    private HistoryBranchLayout? _branchLayout;

    private SourceEntry? _trayHistorySource;

    private HistorySourceRoot? _historySourceRoot;

    private readonly DispatcherTimer _traySaveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };

    private bool _traysReady, _refreshingTrays, _traysExpanded, _sourceLibraryExpanded;

    private Guid? _sourceTrayFolder;

    private string _sourceScrollKey = "";

    private readonly Dictionary<VirtualizingTray, int> _pendingTrayRestores = [];

    private int _trayRestoreRevision;

    internal bool SelectionUpdatesPaused => _restoringHistory;

    internal void WithSelectionPaused(Action change)
    {
        bool wasPaused = _restoringHistory;
        _restoringHistory = true;
        try { change(); }
        finally { _restoringHistory = wasPaused; }
    }

    internal void ConfigureSourceTray() => WithSelectionPaused(() => _editor.sourceStrip.ItemsSource = _traySources);

    internal void ResetSourceFolder() => _sourceTrayFolder = null;

    internal void ClearSourceTray()
    {
        _traySources.Clear();
        _editor.sourceStrip.SetLayout([]);
    }

    internal void RefreshHistoryFor(SourceEntry source)
    {
        if (source == _trayHistorySource) RefreshHistoryLayout();
    }

    internal void SelectProcessingBasis(SourceEntry? source, int basis)
    {
        if (_restoringHistory) return;
        WithSelectionPaused(() =>
        {
            _editor.sourceStrip.SelectedItem = basis == 0 ? source : null;
            _editor.generationStrip.SelectedItem = basis == 1 ? source?.SelectedGeneration : _historySourceRoot;
        });
    }

    internal void DetachCurrentHistory()
    {
        if (_trayHistorySource is not null) _trayHistorySource.Generations.CollectionChanged -= HistoryChanged;
        _trayHistorySource = null;
    }

    internal void ReleaseDocument(SourceEntry? activeSource)
    {
        _editor.sourceStrip.SelectedItem = null;
        _editor.generationStrip.SelectedItem = null;
        _editor.generationStrip.ItemsSource = null;
        _pendingTrayRestores.Clear();
        var traySource = _trayHistorySource;
        DetachCurrentHistory();
        activeSource?.UnloadHistory();
        if (traySource != activeSource) traySource?.UnloadHistory();
        _loadedBranchSource = null;
        _loadedBranch = null;
        _historySourceRoot = null;
        _branchLayout = null;
        _historyItems.Clear();
        _editor.generationStrip.SetLayout([]);
        ClearDrag();
    }

    internal void InitializeHistoryTrays()
    {
        try { _trayState = _trayStore.Load(); }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        { Host.Notify(Localization.Text("CouldNotRestoreTrayLayoutPrefix") + ex.Message, NotificationKind.Warning); }
        _trayState.Height = double.IsFinite(_trayState.Height) ? Math.Max(120, _trayState.Height) : 120;
        _trayState.ExpandedHeight = double.IsFinite(_trayState.ExpandedHeight) ? Math.Max(160, _trayState.ExpandedHeight) : 290;
        _editor.trayHeight.Height = new GridLength(_trayState.Height);
        _traysExpanded = _trayState.Height > 150;
        _traySaveTimer.Tick += (_, _) => { _traySaveTimer.Stop(); SaveTrayState(); };
        _editor.trayHeightSplitter.DragCompleted += (_, _) => CommitTrayHeight();
        UpdateSourceLibrary();
        _editor.trayHeightSplitter.PreviewMouseDoubleClick += (_, e) =>
        {
            double target = _traysExpanded ? 120 : _trayState.ExpandedHeight;
            _editor.trayHeight.Height = new GridLength(Math.Min(target, MaximumTrayHeight()));
            CommitTrayHeight(); e.Handled = true;
        };
        _editor.previewArea.SizeChanged += (_, _) =>
        {
            if (!_editor.previewArea.IsVisible || _editor.previewArea.ActualHeight <= 246) return;
            double maximum = MaximumTrayHeight();
            if (_editor.trayHeight.Height.Value > maximum) { _editor.trayHeight.Height = new GridLength(maximum); CommitTrayHeight(); }
        };
        _editor.generationStrip.ScrollPositionChanged += (_, _) => { RememberTrayScroll(); ScheduleTraySave(); };
        _editor.sourceStrip.ScrollPositionChanged += (_, _) => { RememberTrayScroll(); ScheduleTraySave(); };
        Host._sources.CollectionChanged += (_, _) => RefreshSourceTray();
        Host._workspaceSources.CollectionChanged += (_, _) => RefreshSourceTray();
        _editor.generationStrip.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (_editor._processing) return;
            if (ItemsControl.ContainerFromElement(_editor.generationStrip, e.OriginalSource as DependencyObject) is not ListBoxItem { Content: GenerationEntry generation } card) return;
            card.IsSelected = true;
            OpenHistoryContextMenu(generation, card); e.Handled = true;
        };
        _editor.generationStrip.PreviewKeyDown += (_, e) =>
        {
            if (_editor._processing || _editor.generationStrip.SelectedItem is not GenerationEntry selected) return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Apps || key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift)
            { OpenHistoryContextMenu(selected, _editor.generationStrip); e.Handled = true; }
            else if (key == Key.Delete)
            { RemoveGenerationByUser(selected); e.Handled = true; }
        };
        _editor.generationStrip.InputBindings.Add(new KeyBinding(ApplicationCommands.Copy, Key.C, ModifierKeys.Control));
        _editor.generationStrip.InputBindings.Add(new KeyBinding(ApplicationCommands.Paste, Key.V, ModifierKeys.Control));
        _editor.generationStrip.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy,
            (_, _) => CopySelectedResultToClipboard(),
            (_, e) => { e.CanExecute = !_editor._processing && _editor.generationStrip.SelectedItem is GenerationEntry; e.Handled = true; }));
        _editor.generationStrip.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste,
            (_, _) => PasteClipboardAsBranch(),
            (_, e) => { e.CanExecute = !_editor._processing && _editor._activeSource is not null && ClipboardHasImage(); e.Handled = true; }));
        _editor.generationStrip.DragLeave += (_, _) => _editor.generationStrip.InsertionY = null;
        Host.Closing += (_, e) => { if (!e.Cancel) { _traySaveTimer.Stop(); RememberTrayScroll(); SaveTrayState(); } };
        _editor.Loaded += (_, _) =>
        {
            if (!_editor.previewArea.IsVisible || _editor.previewArea.ActualHeight <= 246) return;
            _editor.trayHeight.Height = new GridLength(Math.Min(_trayState.Height, MaximumTrayHeight()));
            CommitTrayHeight(); RestoreTrayScroll();
        };
        _traysReady = true;
    }

    private double MaximumTrayHeight() => Math.Max(120, _editor.previewArea.ActualHeight - 246);

    internal void ToggleSourceLibrary(object sender, RoutedEventArgs e)
    {
        _sourceLibraryExpanded = !_sourceLibraryExpanded;
        UpdateSourceLibrary();
        RefreshSourceTray();
        if (_sourceLibraryExpanded) RestoreTrayScroll();
    }

    private void UpdateSourceLibrary()
    {
        _editor.sourceLibraryHeight.Height = new GridLength(_sourceLibraryExpanded ? 180 : 0);
        _editor.sourceTray.Visibility = _sourceLibraryExpanded ? Visibility.Visible : Visibility.Collapsed;
        _editor.sourceLibraryToggle.Content = Localization.Text(_sourceLibraryExpanded ? "AssetLibraryExpanded" : "AssetLibraryCollapsed");
    }

    private void CommitTrayHeight()
    {
        RememberTrayScroll();
        double height = Math.Clamp(_editor.trayHeight.ActualHeight, 120, MaximumTrayHeight());
        // DragCompleted reports the updated row definition even before the next layout pass.
        if (_editor.trayHeight.Height.IsAbsolute) height = Math.Clamp(_editor.trayHeight.Height.Value, 120, MaximumTrayHeight());
        _trayState.Height = height;
        if (height > 150) _trayState.ExpandedHeight = height;
        _trayPreferencesDirty = true;
        bool expanded = height > 150;
        if (expanded != _traysExpanded) { _traysExpanded = expanded; RefreshSourceTray(); RestoreTrayScroll(); }
        ScheduleTraySave();
    }

    private BranchPresentation CurrentBranchPresentation()
    {
        return _trayHistorySource is { } source ? BranchFor(source) : new();
    }

    internal void ConfigureHistorySource(SourceEntry source)
    {
        RememberTrayScroll();
        if (_trayHistorySource is not null) _trayHistorySource.Generations.CollectionChanged -= HistoryChanged;
        _trayHistorySource = source;
        _historySourceRoot = new HistorySourceRoot(source);
        _editor.generationStrip.ItemsSource = _historyItems;
        source.Generations.CollectionChanged += HistoryChanged;
        RefreshHistoryLayout();
        ChooseSourceFolder(source);
        RestoreTrayScroll();
    }

    private void HistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A new result must win over a deferred selection/scroll restoration from opening its source.
        _pendingTrayRestores.Remove(_editor.generationStrip);
        RefreshHistoryLayout();
    }

    internal void RefreshHistoryLayout()
    {
        bool restoring = _restoringHistory; _restoringHistory = true;
        try
        {
            if (_trayHistorySource is null)
            {
                _historyItems.Clear(); _editor.generationStrip.SetLayout([]); return;
            }
            _branchLayout = HistoryBranchLayout.Build(_trayHistorySource.Generations.ToArray(), CurrentBranchPresentation());
            var sourceBounds = new Rect(0, 4, 124, 68);
            var bounds = new List<Rect> { sourceBounds };
            bounds.AddRange(_trayHistorySource.Generations.Select(g => OffsetHistoryBounds(_branchLayout.Bounds[g.Id])));
            var lines = _branchLayout.Connections.Select(link =>
            {
                var parent = OffsetHistoryBounds(_branchLayout.Bounds[link.Parent]); var child = OffsetHistoryBounds(_branchLayout.Bounds[link.Child]);
                return (new Point(parent.Right, parent.Y + parent.Height / 2), new Point(child.Left, child.Y + child.Height / 2));
            }).ToList();
            foreach (var root in _branchLayout.Roots)
            {
                var child = OffsetHistoryBounds(_branchLayout.Bounds[root]);
                lines.Add((new Point(sourceBounds.Right, sourceBounds.Y + sourceBounds.Height / 2), new Point(child.Left, child.Y + child.Height / 2)));
            }
            _historyItems.Clear();
            _historyItems.Add(_historySourceRoot!);
            foreach (var generation in _trayHistorySource.Generations) _historyItems.Add(generation);
            _editor.generationStrip.SetLayout(bounds, lines);
            _editor.generationStrip.SelectedItem = _editor.ProcessingBasis == 0 ? _historySourceRoot : _trayHistorySource.SelectedGeneration;
        }
        finally { _restoringHistory = restoring; }
    }

    private static Rect OffsetHistoryBounds(Rect bounds) => new(bounds.X + 142, bounds.Y, bounds.Width, bounds.Height);

    private void PromoteHistoryBranch(GenerationEntry entry, int? insertion = null)
    {
        if (_branchLayout is null || _trayHistorySource?.Generations.Contains(entry) != true) return;
        var source = _trayHistorySource;
        if (_branchLayout.RootOf(entry.Id) == entry.Id && insertion is null) return;
        var transaction = _historyUndo.Begin(source, CurrentBranchPresentation());
        try
        {
            var branch = CurrentBranchPresentation();
            _branchLayout.Promote(entry.Id, branch, insertion);
            _trayStore.SaveBranch(source.Id, branch);
            RefreshHistoryLayout(); _editor.generationStrip.ScrollIntoView(entry); WriteTrayState();
            transaction.Commit();
        }
        catch (Exception ex)
        {
            Host._mainProgress?.Dispose();
            try { RestoreHistorySource(transaction.Rollback()); }
            catch (Exception rollback) { ex = new AggregateException(ex, rollback); }
            ShowHistoryFailure(Localization.Text("MoveBranch"), ex);
        }
    }

    private ContextMenu OpenHistoryContextMenu(GenerationEntry generation, FrameworkElement target)
    {
        var menu = new ContextMenu();
        menu.Resources.MergedDictionaries.Add(_editor.Resources);
        var detach = new MenuItem { Header = Localization.Text("MoveToASeparateBranch") };
        detach.IsEnabled = _branchLayout?.DisplayParents.GetValueOrDefault(generation.Id) is not null;
        detach.Click += (_, _) => PromoteHistoryBranch(generation);
        var macro = new MenuItem { Header = Localization.Text("CreateMacroAction") };
        macro.Click += (_, _) => CreateMacro(generation);
        var merge = new MenuItem { Header = Localization.Text("MergeIntoBranch") };
        if (_trayHistorySource is { } source && _branchLayout is { } layout)
            foreach (var leaf in source.Generations.Where(g => layout.RootOf(g.Id) != layout.RootOf(generation.Id) &&
                !source.Generations.Any(child => layout.DisplayParents[child.Id] == g.Id)))
            {
                var targetLeaf = leaf;
                var item = new MenuItem { Header = targetLeaf.Label };
                item.Click += (_, _) => MergeHistoryBranches(generation, targetLeaf);
                merge.Items.Add(item);
            }
        merge.IsEnabled = merge.Items.Count > 0;
        var bake = new MenuItem { Header = Localization.Text("Bake") };
        bake.Click += (_, _) => BakeHistoryResult(generation);
        var remove = new MenuItem { Header = generation.RemoveDescription };
        remove.Click += (_, _) => RemoveGenerationByUser(generation);
        menu.Items.Add(detach); menu.Items.Add(merge); menu.Items.Add(macro);
        menu.Items.Add(new Separator()); menu.Items.Add(bake); menu.Items.Add(remove);
        menu.PlacementTarget = target; menu.IsOpen = true;
        return menu;
    }

    private bool IsHistoryDropTarget(DependencyObject? origin)
    {
        for (var current = origin; current is not null; current = ImageEditorPage.ParentOf(current)) if (ReferenceEquals(current, _editor.generationStrip)) return true;
        return false;
    }

    internal bool HistoryDrop(DragEventArgs e, bool commit)
    {
        _editor.generationStrip.InsertionY = null;
        if (!e.Data.GetDataPresent(GenerationDragFormat) || !IsHistoryDropTarget(e.OriginalSource as DependencyObject)) return false;
        bool supported = TryGetDraggedGeneration(e.Data, out var source, out var generation);
        bool valid = !_editor._processing && supported && source == _editor._activeSource;
        if (valid && _branchLayout is { } branch &&
            ItemsControl.ContainerFromElement(_editor.generationStrip, e.OriginalSource as DependencyObject) is ListBoxItem { Content: GenerationEntry target } &&
            branch.RootOf(target.Id) != branch.RootOf(generation.Id))
        {
            e.Effects = DragDropEffects.Copy; e.Handled = true;
            if (commit) MergeHistoryBranches(generation, target);
            return true;
        }
        int insertion = 0;
        if (valid && _branchLayout is { } layout)
        {
            double y = e.GetPosition(_editor.generationStrip).Y + _editor.generationStrip.ScrollPosition.Y;
            double boundary = 0;
            var rootBounds = layout.RootBounds;
            foreach (var root in layout.Roots.Where(id => id != generation.Id))
            {
                var bounds = rootBounds[root];
                if (y >= bounds.Top && y <= bounds.Bottom) { valid = false; break; }
                if (y > bounds.Bottom) { insertion++; boundary = bounds.Bottom + 30; }
                else { boundary = Math.Min(boundary == 0 ? bounds.Top - 3 : boundary, bounds.Top - 3); break; }
            }
            if (valid) _editor.generationStrip.InsertionY = boundary;
            var point = e.GetPosition(_editor.generationStrip);
            if (!commit && point.Y < 18) _editor.generationStrip.Panel?.LineUp();
            else if (!commit && point.Y > _editor.generationStrip.ActualHeight - 18) _editor.generationStrip.Panel?.LineDown();
        }
        e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        if (commit && valid && _branchLayout is not null && TryGetDraggedGeneration(e.Data, out _, out var moved))
        {
            PromoteHistoryBranch(moved, insertion);
        }
        if (commit) _editor.generationStrip.InsertionY = null;
        return true;
    }

    internal void ChooseSourceFolder(SourceEntry source)
    {
        if (Host._workspaceSourceProject is { } project && Host._workspaceCatalog is { } catalog)
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
            _trayPreferencesDirty = true;
        }
        else _sourceTrayFolder = null;
        RefreshSourceTray();
    }

    private void NavigateSourceFolder(Guid? folder, bool toggle = false)
    {
        RememberTrayScroll();
        _sourceTrayFolder = folder;
        if (Host._workspaceSourceProject is { } project)
        {
            _trayState.CurrentFolders[project] = folder;
            if (Host._workspaceCatalog?.State is { View: "project" } state && state.ProjectId == project) state.FolderId = folder;
        }
        if (toggle && folder is { } id)
        { if (!_trayState.CollapsedFolders.Add(id)) _trayState.CollapsedFolders.Remove(id); }
        _trayPreferencesDirty = true;
        RefreshSourceTray(); RestoreTrayScroll(); ScheduleTraySave();
    }

    internal void TrayFolderClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TrayFolderHeader header) header.Activate?.Invoke();
        e.Handled = true;
    }

    internal void RefreshSourceTray()
    {
        if (!_traysReady || _refreshingTrays) return;
        _refreshingTrays = true;
        bool restoring = _restoringHistory; _restoringHistory = true;
        try
        {
            RememberTrayScroll();
            var items = new List<object>(); var positions = new List<Rect>();
            var sources = (Host._workspaceReady ? Host._workspaceSources : Host._sources).ToArray();
            var state = Host._workspaceCatalog?.State;
            var project = state?.Projects.FirstOrDefault(p => p.Id == Host._workspaceSourceProject);
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
                _editor.sourceCaption.Text = Localization.Text(Host._workspaceSourcesUnassigned ? "Unassigned" : "SourcesHeading");
            }
            else
            {
                if (_sourceTrayFolder is { } current && !state!.Folders.Any(f => f.Id == current && f.ProjectId == project.Id)) _sourceTrayFolder = null;
                BuildSourceBreadcrumb(project);
                if (!_sourceLibraryExpanded) AddImages(InFolder(_sourceTrayFolder), 4, 0);
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
                        items.Add(new TrayFolderHeader
                        {
                            FolderId = folder.Id,
                            Label = (collapsed ? "▸ " : "▾ ") + folder.Name,
                            Activate = () => NavigateSourceFolder(selectedFolder, toggle: true)
                        });
                        positions.Add(new Rect(4 + folder.Depth * 16, y, 280, 26)); y += 28;
                        if (collapsed) continue;
                        var images = InFolder(folder.Id).ToArray();
                        if (images.Length > 0) { AddImages(images, y, folder.Depth); y += 76; }
                        else
                        {
                            items.Add(new TrayFolderHeader { FolderId = folder.Id, Label = Localization.Text("NoImages"), IsEmpty = true });
                            positions.Add(new Rect(20 + folder.Depth * 16, y, 220, 28)); y += 32;
                        }
                        foreach (var child in state!.Folders.Where(f => f.ProjectId == project.Id && f.ParentId == folder.Id).OrderByDescending(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
                            pending.Push((child.Id, child.Name, folder.Depth + 1));
                    }
                }
            }
            if (project is not null && items.Count == 0)
            {
                items.Add(new TrayFolderHeader { Label = Localization.Text("NoImages"), IsEmpty = true });
                positions.Add(new Rect(4, 4, 220, 28));
            }
            string scope = Host._workspaceSourceProject?.ToString() ?? (Host._workspaceSourcesUnassigned ? "unassigned" : "all");
            string location = _sourceLibraryExpanded ? "tree" : _sourceTrayFolder?.ToString() ?? "root";
            _sourceScrollKey = $"sources:{scope}:{location}";
            _traySources.Clear(); foreach (var item in items) _traySources.Add(item);
            _editor.sourceStrip.SetLayout(positions);
            if (!ReferenceEquals(_editor.sourceStrip.ItemsSource, _traySources)) _editor.sourceStrip.ItemsSource = _traySources;
            _editor.sourceStrip.SelectedItem = _editor.ProcessingBasis == 0 && items.Contains(_editor._activeSource!) ? _editor._activeSource : null;
            QueueTrayScrollRestore(_editor.sourceStrip, _sourceScrollKey);
        }
        finally { _restoringHistory = restoring; _refreshingTrays = false; }
    }

    internal void BuildSourceBreadcrumb(WorkspaceProject project)
    {
        _editor.sourceCaption.Inlines.Clear();
        void Add(string name, Guid? folder)
        {
            if (_editor.sourceCaption.Inlines.Count > 0) _editor.sourceCaption.Inlines.Add(new Run(" › "));
            var link = new Hyperlink(new Run(name)); link.SetResourceReference(Hyperlink.ForegroundProperty, "Ts.TextSecondary");
            link.Click += (_, _) => NavigateSourceFolder(folder);
            _editor.sourceCaption.Inlines.Add(link);
        }
        Add(project.Name, null);
        var path = new Stack<WorkspaceFolder>(); var seen = new HashSet<Guid>(); Guid? id = _sourceTrayFolder;
        while (id is { } current && seen.Add(current) && Host._workspaceCatalog!.State.Folders.FirstOrDefault(f => f.Id == current && f.ProjectId == project.Id) is { } folder)
        { path.Push(folder); id = folder.ParentId; }
        _editor.sourceCaption.ToolTip = string.Join(" › ", new[] { project.Name }.Concat(path.Select(f => f.Name)));
        while (path.TryPop(out var folder)) Add(folder.Name, folder.Id);
    }

    internal void RememberTrayScroll()
    {
        if (!_traysReady) return;
        if (_trayHistorySource is { } source && _editor.generationStrip.Panel is not null && !_pendingTrayRestores.ContainsKey(_editor.generationStrip))
            _pendingTrayScroll["results:" + source.Id] = _editor.generationStrip.ScrollPosition;
        if (_sourceScrollKey.Length > 0 && _editor.sourceStrip.Panel is not null && !_pendingTrayRestores.ContainsKey(_editor.sourceStrip))
            _pendingTrayScroll[_sourceScrollKey] = _editor.sourceStrip.ScrollPosition;
    }

    private void RestoreTrayScroll()
    {
        if (_trayHistorySource is { } source) QueueTrayScrollRestore(_editor.generationStrip, "results:" + source.Id);
        QueueTrayScrollRestore(_editor.sourceStrip, _sourceScrollKey);
    }

    private void QueueTrayScrollRestore(VirtualizingTray tray, string key)
    {
        if (key.Length == 0) return;
        if (!_pendingTrayScroll.TryGetValue(key, out var position))
        {
            try { position = _trayStore.ReadScroll(key); }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
            { Host.Notify(Localization.Text("CouldNotRestoreTrayLayoutPrefix") + ex.Message, NotificationKind.Warning); }
        }
        int revision = ++_trayRestoreRevision;
        _pendingTrayRestores[tray] = revision;
        // ListBox queues its own ScrollIntoView while restoring selection. Restore after it,
        // without letting that transient movement overwrite the saved position.
        _editor.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            if (_pendingTrayRestores.GetValueOrDefault(tray) != revision) return;
            tray.UpdateLayout(); tray.RestoreScroll(position);
            _pendingTrayRestores.Remove(tray);
            if (tray.ScrollPosition != position) _pendingTrayScroll[key] = tray.ScrollPosition;
        });
    }

    private void ScheduleTraySave() { if (!_traysReady) return; _traySaveTimer.Stop(); _traySaveTimer.Start(); }

    private void SaveTrayState()
    {
        try
        {
            WriteTrayState();
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        { Host.Notify(Localization.Text("CouldNotSaveTrayLayoutPrefix") + ex.Message, NotificationKind.Error); }
    }

    private void WriteTrayState()
    {
        if (_trayPreferencesDirty)
        {
            _trayStore.SavePreferences(_trayState);
            _trayPreferencesDirty = false;
        }
        foreach (var (key, position) in _pendingTrayScroll)
            _trayStore.SaveScroll(key, position);
        _pendingTrayScroll.Clear();
    }
}
