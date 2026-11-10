using System.Text.Json;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Tessera.Desktop;
using Tessera.WpfStyles.Themes;

namespace Tessera.RegressionTests;

public static class UnrealSyncChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Unreal bridge transfers PNG, rejects incomplete downloads and tracks revisions", RoundTrip);
        check("Unreal library mirrors paths, keeps history and resumes a save after restart", ProjectLibrary);
    }

    private static void RoundTrip()
    {
        string root = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "tessera-unreal-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "Game.uproject");
            File.WriteAllText(project, "{}");
            var bridge = new UnrealSyncBridge(Path.Combine(root, "library", "unreal-sync.json"));
            bridge.SelectProject(project);
            bridge.InstallPlugin();
            var python = Path.Combine(root, "Plugins", "TesseraSync", "Content", "Python");
            if (!File.Exists(Path.Combine(python, "tessera_project.py")) || !File.Exists(Path.Combine(python, "init_unreal.py")))
                throw new Exception("Standalone installer omitted the project sync plugin");
            using var image = new Bitmap(2, 2);
            image.SetPixel(0, 0, System.Drawing.Color.FromArgb(128, 220, 30, 50));
            string source = Path.Combine(root, "source.png");
            image.Save(source, System.Drawing.Imaging.ImageFormat.Png);
            string unrealPath = bridge.SendPng(source, Guid.Parse("11111111-2222-3333-4444-555555555555"), "Stone.png");
            if (!unrealPath.StartsWith("/Game/TesseraSync/T_Stone_", StringComparison.Ordinal)) throw new Exception("Unexpected Unreal path");
            string name = unrealPath.Split('/')[^1];
            string outgoing = Path.Combine(bridge.BridgeDirectory, "to_unreal");
            var sent = JsonSerializer.Deserialize<UnrealSyncBridge.Transfer>(File.ReadAllText(Path.Combine(outgoing, name + ".json")))!;
            if (sent.Sha256 != UnrealSyncBridge.Hash(Path.Combine(outgoing, name + ".png"))) throw new Exception("Outgoing hash mismatch");
            bridge.SendPng(source, Guid.Parse("11111111-2222-3333-4444-555555555555"), "Stone.png");
            var resent = JsonSerializer.Deserialize<UnrealSyncBridge.Transfer>(File.ReadAllText(Path.Combine(outgoing, name + ".json")))!;
            if (resent.Revision == sent.Revision || resent.Sha256 != sent.Sha256) throw new Exception("Explicit resend was not distinguishable");

            string incoming = Path.Combine(bridge.BridgeDirectory, "to_tessera");
            Directory.CreateDirectory(incoming);
            string receivedPng = Path.Combine(incoming, name + ".png");
            File.Copy(source, receivedPng);
            sent.Sha256 = UnrealSyncBridge.Hash(receivedPng);
            File.WriteAllText(Path.Combine(incoming, name + ".json"), JsonSerializer.Serialize(sent));
            if (bridge.PendingFromUnreal().Count != 1) throw new Exception("Valid incoming PNG was not found");
            File.AppendAllText(receivedPng, "corrupt");
            if (bridge.PendingFromUnreal().Count != 0) throw new Exception("Partial or changed PNG was accepted");
            File.Copy(source, receivedPng, true);
            bridge.MarkReceived(sent);
            if (bridge.PendingFromUnreal().Count != 0) throw new Exception("Received revision was duplicated");
            image.SetPixel(1, 1, System.Drawing.Color.Blue);
            image.Save(receivedPng, System.Drawing.Imaging.ImageFormat.Png);
            sent.Sha256 = UnrealSyncBridge.Hash(receivedPng);
            File.WriteAllText(Path.Combine(incoming, name + ".json"), JsonSerializer.Serialize(sent));
            if (new UnrealSyncBridge(Path.Combine(root, "library", "unreal-sync.json")).PendingFromUnreal().Count != 1)
                throw new Exception("Updated Unreal texture was not detected after restart");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void ProjectLibrary()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { VerifyProjectLibrary(); }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(45))) throw new Exception("Unreal library check timed out");
        if (failure is not null) throw new Exception("Unreal library check failed: " + failure, failure);
    }

    private static void VerifyProjectLibrary()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string root = Path.GetFullPath(Path.Combine("artifacts", "unreal-library-" + Guid.NewGuid().ToString("N")));
        string libraryRoot = Path.Combine(root, "Library");
        string project = Path.Combine(root, "Game.uproject");
        string bridge = Path.Combine(root, "Saved", "TesseraSync", "v2");
        Directory.CreateDirectory(bridge);
        File.WriteAllText(project, "{}");
        const string target = "/Game/A/Nested/Stone.Stone", other = "/Game/B/Stone.Stone", moved = "/Game/A/Rock.Rock";
        string[] paths = [target, other, .. Enumerable.Range(0, 800).Select(i => $"/Game/B/T_{i}.T_{i}")];
        void Catalog(string[] textures, Dictionary<string, string>? redirects = null, string revision = "rev1")
        {
            string path = Path.Combine(bridge, "catalog.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                version = 2, folders = new[] { "/Game", "/Game/A/Empty", "/Game/Levels/Maps", "/Game/__ExternalActors__/Ignored" },
                textures = textures.Select(p => new { path = p, revision }),
                redirects = redirects ?? []
            }));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        void Poll(MainWindow window) => typeof(MainWindow).GetMethod("PollUnrealProjects", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        Task Save(MainWindow window) => (Task)typeof(MainWindow).GetMethod("SaveNavigationDocumentAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        try
        {
            Catalog(paths);
            new WorkspaceCatalog(libraryRoot).CreateProject("Local");
            var sync = new UnrealSyncBridge(Path.Combine(libraryRoot, "unreal-sync.json"));
            sync.SelectProject(project);
            sync.InstallPlugin();
            Guid assetId;
            Guid createdId;
            Guid createdResultId;
            string createdRequestFolder;
            string requestFolder;
            var window = new MainWindow(libraryRoot);
            try
            {
                window.EnableWorkspacePrototype(); window.Show(); Poll(window);
                var workspace = window.Workspace!;
                var unreal = workspace.State.Projects.Single(p => p.IsUnreal);
                window.Browser!.Navigate("project", unreal.Id);
                window.Browser.RebuildTree();
                Poll(window);
                Pump(); window.UpdateLayout();
                var addProject = (Button)window.Browser.FindName("addProjectButton");
                var projectMenu = addProject.ContextMenu!;
                Require(projectMenu.Items.OfType<MenuItem>().Select(item => item.Name)
                    .SequenceEqual(["localProjectMenuItem", "unrealProjectMenuItem"]),
                    "The project plus does not offer local and Unreal projects");
                addProject.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                Require(projectMenu.IsOpen, "The project plus did not open its menu");
                projectMenu.IsOpen = false;
                var statusPanel = (Grid)window.Browser.FindName("unrealStatusPanel");
                var statusDot = (System.Windows.Shapes.Ellipse)window.Browser.FindName("unrealStatusDot");
                Require(statusPanel.Visibility == Visibility.Visible && statusDot.Fill == System.Windows.Media.Brushes.Transparent &&
                    statusDot.Stroke is SolidColorBrush { Color.R: > 200, Color.G: > 100, Color.B: < 100 },
                    "Disconnected Unreal status is not an orange outline");
                var tree = (TreeView)window.Browser.FindName("projectTree");
                var rootNode = tree.Items.OfType<TreeViewItem>().Single(n => n.Tag == unreal);
                Require(((Border)rootNode.Template.FindName("HeaderFrame", rootNode)).ActualHeight <= 21,
                    "Project tree rows are too tall");
                var icon = ((Grid)rootNode.Header).Children.OfType<System.Windows.Shapes.Path>().Single();
                foreach (var theme in new[] { TesseraTheme.Dark, TesseraTheme.Light })
                {
                    AppearanceResources.Apply(theme);
                    window.Browser.LayoutTransform = new ScaleTransform(1.25, 1.25);
                    Pump(); window.UpdateLayout();
                    Require(icon is { Width: 18, Height: 18, Data: not null, Fill: not null } && icon.Margin.Right == 6, "Unreal icon is missing or malformed");
                    Require(tree.Items.OfType<TreeViewItem>().Single(n => n.Tag is WorkspaceProject { IsUnreal: false }).Header is TextBlock &&
                        ((TreeViewItem)rootNode.Items[0]).Header is TextBlock, "The Unreal mark leaked onto a local project or folder");
                }
                var list = (ListBox)window.Browser.FindName("assetList");
                Require(list.Items.Count == paths.Length && list.ItemContainerGenerator.ContainerFromIndex(paths.Length - 1) is null, "Large Unreal view was not virtualized");
                var viewButtons = new[] { "assetViewTiles", "assetViewList", "assetViewSmallTiles" }
                    .Select(name => (RadioButton)window.Browser.FindName(name)).ToArray();
                var retained = (WorkspaceAsset)list.Items[0];
                list.SelectedItem = retained;
                foreach (int mode in new[] { 1, 2, 0 })
                {
                    viewButtons[mode].IsChecked = true; Pump(); window.UpdateLayout();
                    Require(list.Items.Count == paths.Length && ReferenceEquals(list.SelectedItem, retained) &&
                        list.ItemContainerGenerator.ContainerFromIndex(paths.Length - 1) is null &&
                        new WorkspaceCatalog(libraryRoot).State.AssetView == mode &&
                        viewButtons.Count(button => button.IsChecked == true) == 1 && viewButtons[mode].ToolTip is not null,
                        "Changing asset view lost selection, persistence or virtualization");
                    var first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0)!;
                    var second = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1)!;
                    var a = first.TranslatePoint(new System.Windows.Point(), list);
                    var b = second.TranslatePoint(new System.Windows.Point(), list);
                    Require(mode == 1 ? Math.Abs(a.X - b.X) < 2 && b.Y > a.Y : b.X > a.X && Math.Abs(a.Y - b.Y) < 2,
                        "Asset view did not arrange cards as a list or grid");
                }
                list.ScrollIntoView(list.Items[paths.Length - 1]); Pump(); window.UpdateLayout();
                Require(list.ItemContainerGenerator.ContainerFromIndex(paths.Length - 1) is not null &&
                    list.ItemContainerGenerator.ContainerFromIndex(0) is null,
                    "Scrolling the virtualized asset view did not realize the last item");
                list.ScrollIntoView(retained); Pump(); window.UpdateLayout();
                Require(list.ItemContainerGenerator.ContainerFromIndex(0) is not null,
                    "Scrolling the virtualized asset view did not return to the first item");
                Require(workspace.State.Folders.All(f => f.Name is not ("Empty" or "Levels" or "Maps" or "__ExternalActors__")) &&
                    workspace.State.Folders.Any(f => f.UnrealPath == "/Game/A/Nested"), "The tree must contain texture folders and their ancestors only");
                Require(workspace.State.Assets.Count(a => a.Name == "Stone") == 2, "Identical names were merged");
                var asset = workspace.State.Assets.Single(a => a.Unreal?.Path == target);
                assetId = asset.Id;
                list.SelectedItem = asset;
                Require(((StackPanel)window.Browser.FindName("assetTagsPanel")).Visibility == Visibility.Collapsed &&
                    ((StackPanel)window.Browser.FindName("assetManagementPanel")).Visibility == Visibility.Collapsed &&
                    list.ContextMenu.Items.OfType<MenuItem>().Where(i => i.Visibility == Visibility.Visible)
                        .Select(i => i.Tag).OrderBy(tag => tag).SequenceEqual(new object[] { "create-unreal", "delete-unreal", "move-unreal", "open" }),
                    "Unreal selection shows the wrong asset actions");
                var local = new WorkspaceAsset { Name = "Local image" };
                workspace.State.Assets.Add(local);
                window.Browser.Navigate("all"); list.SelectedItem = asset;
                Require(((StackPanel)window.Browser.FindName("assetManagementPanel")).Visibility == Visibility.Collapsed,
                    "Linked textures expose management actions in All assets");
                list.SelectedItem = local;
                Require(((StackPanel)window.Browser.FindName("assetTagsPanel")).Visibility == Visibility.Visible &&
                    ((StackPanel)window.Browser.FindName("assetManagementPanel")).Visibility == Visibility.Visible,
                    "Local assets lost their management actions");
                window.Browser.Navigate("project", unreal.Id); list.SelectedItem = asset;
                string read = Path.Combine(bridge, "requests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(read);
                string export = Path.Combine(read, "unreal.png");
                using (var png = new Bitmap(8, 8)) png.Save(export, System.Drawing.Imaging.ImageFormat.Png);
                File.WriteAllText(Path.Combine(read, "request.json"), JsonSerializer.Serialize(new { version = 2, operation = "read", assetId, path = target, revision = "rev1" }));
                File.WriteAllText(Path.Combine(read, "response.json"), JsonSerializer.Serialize(new { status = "read", revision = "rev1", sha256 = UnrealSyncBridge.Hash(export) }));
                File.WriteAllText(Path.Combine(bridge, "editor.json"), "{}");
                Task opening = window.OpenWorkspaceAssetAsync(asset);
                Poll(window);
                Require(statusDot.Fill is SolidColorBrush { Color.G: > 150, Color.R: < 100 } &&
                    ReferenceEquals(statusDot.Fill, statusDot.Stroke),
                    "Connected Unreal status is not a solid green dot");
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!opening.IsCompleted && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(10); }
                Require(opening.IsCompletedSuccessfully && asset.Unreal!.LoadedRevision == "rev1", "Unreal texture was not opened from the mailbox");
                var active = (SourceEntry)typeof(MainWindow).GetField("_activeSource", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                using (var pixels = new Bitmap(4, 4))
                    active.Generations.Add(new ImageLibrary(libraryRoot).SaveGeneration(active, pixels, null, new GenerationEntry { Operation = "Test result" }));
                Require(asset.Unreal!.ResultRevisions.GetValueOrDefault(active.Generations[0].Id) == "rev1", "A local result lost its original Unreal revision");
                File.Delete(Path.Combine(bridge, "editor.json"));
                Get<ListBox>(window, "generationStrip").SelectedItem = active.Generations[0];
                Require(window.ProcessingBasis == 1, "The new texture check did not select the processed result");
                bool duplicateRejected = false, invalidFolderRejected = false, destinationAccepted = false;
                var dialogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                dialogTimer.Tick += (_, _) =>
                {
                    var dialog = window.OwnedWindows.Cast<Window>().FirstOrDefault(w => w.GetType().Name == "UnrealTextureDialog");
                    if (dialog is null) return;
                    dialogTimer.Stop();
                    T Input<T>(string name) => (T)dialog.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                    var folder = Input<ComboBox>("FolderInput");
                    var name = Input<TextBox>("NameInput");
                    var confirm = Input<Button>("Confirm");
                    folder.Text = "/Game/A/Nested";
                    name.Text = "Stone";
                    duplicateRejected = !confirm.IsEnabled;
                    name.Text = "T_FromTessera";
                    folder.Text = "/Outside";
                    Pump();
                    invalidFolderRejected = !confirm.IsEnabled;
                    folder.Text = "/Game/Imported";
                    name.Text = "T_FromTessera";
                    Pump();
                    destinationAccepted = confirm.IsEnabled;
                    if (confirm.IsEnabled) confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else dialog.Close();
                };
                dialogTimer.Start();
                try
                {
                    var creation = (Task)typeof(MainWindow).GetMethod("CreateUnrealTextureAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
                    while (!creation.IsCompleted) Pump();
                    creation.GetAwaiter().GetResult();
                }
                finally { dialogTimer.Stop(); }
                Require(duplicateRejected && invalidFolderRejected && destinationAccepted,
                    $"The texture dialog validation failed: occupied={duplicateRejected}, outside /Game={invalidFolderRejected}, new destination={destinationAccepted}");
                var created = workspace.State.Assets.Single(a => a.Unreal?.Path == "/Game/Imported/T_FromTessera.T_FromTessera");
                createdId = created.Id;
                Require(created.Unreal!.CreatePending && createdId != assetId && workspace.State.Links.Any(l => l.AssetId == createdId && l.ProjectId == unreal.Id),
                    "An offline new texture lost its link or replaced the source asset");
                createdRequestFolder = Directory.EnumerateDirectories(Path.Combine(bridge, "requests")).Single(p =>
                {
                    using var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(p, "request.json")));
                    return request.RootElement.GetProperty("assetId").GetGuid() == createdId;
                });
                using (var payload = new Bitmap(Path.Combine(createdRequestFolder, "local.png")))
                    Require(payload.Width == 4 && payload.Height == 4, "Creation sent the original instead of the selected result");
                var pendingSource = (SourceEntry)typeof(MainWindow).GetField("_activeSource", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                using (var pixels = new Bitmap(2, 2))
                {
                    var result = new ImageLibrary(libraryRoot).SaveGeneration(pendingSource, pixels, null, new GenerationEntry { Operation = "Offline edit" });
                    pendingSource.Generations.Add(result);
                    createdResultId = result.Id;
                }
                window.OpenWorkspaceAssetAsync(asset).GetAwaiter().GetResult();
                window.ProcessingBasis = 0;
                Save(window).GetAwaiter().GetResult();
                requestFolder = Directory.EnumerateDirectories(Path.Combine(bridge, "requests"))
                    .Single(p => p != createdRequestFolder && File.Exists(Path.Combine(p, "local.png")));
                Require(!File.Exists(Path.Combine(requestFolder, "response.json")), "Offline save was incorrectly acknowledged");
                var stale = workspace.CreateFolder(unreal.Id, null, "Stale empty folder");
                workspace.State.FolderId = stale.Id;
                workspace.Save();
            }
            finally { window.Close(); Pump(); }

            window = new MainWindow(libraryRoot);
            try
            {
                window.EnableWorkspacePrototype(); window.Show();
                Require(window.Workspace!.State.Folders.All(f => f.Name != "Stale empty folder") &&
                    window.Workspace.State.FolderId is null, "Restart retained an empty folder from an older cached tree");
                Poll(window);
                var state = window.Workspace!.State;
                Require(state.Projects.Count(p => p.IsUnreal) == 1 && File.Exists(Path.Combine(requestFolder, "local.png")), "Restart lost the queued save or duplicated the project");
                var created = state.Assets.Single(a => a.Id == createdId);
                Require(created.Unreal!.CreatePending && state.Folders.Any(f => f.UnrealPath == "/Game/Imported"),
                    "Restart pruned an offline new texture or its folder");
                File.Copy(Path.Combine(createdRequestFolder, "local.png"), Path.Combine(createdRequestFolder, "unreal.png"));
                File.WriteAllText(Path.Combine(createdRequestFolder, "response.json"), JsonSerializer.Serialize(new
                {
                    status = "saved", revision = "created1",
                    sha256 = UnrealSyncBridge.Hash(Path.Combine(createdRequestFolder, "unreal.png"))
                }));
                Catalog([.. paths, created.Unreal.Path]); Poll(window);
                Require(!created.Unreal.CreatePending && !created.Unreal.Missing && created.Unreal.LoadedRevision == "created1" &&
                    state.Assets.Count(a => a.Unreal?.Path == created.Unreal.Path) == 1 &&
                    created.Unreal.ResultRevisions.GetValueOrDefault(createdResultId) == "created1",
                    "Confirmation created a duplicate or failed to bind the new texture");
                window.OpenWorkspaceAssetAsync(created).GetAwaiter().GetResult();
                var createdSource = (SourceEntry)typeof(MainWindow).GetField("_activeSource", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                Get<ListBox>(window, "generationStrip").SelectedItem = createdSource.Generations.Single(g => g.Id == createdResultId);
                Save(window).GetAwaiter().GetResult();
                string subsequent = Directory.EnumerateDirectories(Path.Combine(bridge, "requests")).Single(p =>
                {
                    using var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(p, "request.json")));
                    return request.RootElement.GetProperty("assetId").GetGuid() == createdId &&
                        request.RootElement.GetProperty("operation").GetString() == "save";
                });
                using (var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(subsequent, "request.json"))))
                    Require(request.RootElement.GetProperty("operation").GetString() == "save" &&
                        request.RootElement.GetProperty("revision").GetString() == "created1",
                        "Ctrl+S recreated a confirmed texture instead of updating it");
                var asset = state.Assets.Single(a => a.Id == assetId);
                string export = Path.Combine(requestFolder, "unreal.png");
                File.Copy(Path.Combine(requestFolder, "local.png"), export);
                File.WriteAllText(Path.Combine(requestFolder, "response.json"), JsonSerializer.Serialize(new { status = "saved", revision = "rev2", sha256 = UnrealSyncBridge.Hash(export) }));
                Poll(window);
                Require(asset.Unreal!.LoadedRevision == "rev2" && !Directory.Exists(requestFolder), "Restart did not consume the confirmed save");
                Catalog([moved, other], new() { [target] = moved }, "rev2"); Poll(window);
                Require(asset.Unreal.Path == moved && !asset.Unreal.Missing, "Rename redirect lost the linked library identity");
                Require(state.Folders.All(f => f.UnrealPath != "/Game/A/Nested"), "Moving the final texture retained its old folder");
                Catalog([other], revision: "rev3"); Poll(window);
                var retained = new ImageLibrary(libraryRoot).Load().Single(s => s.Id == assetId);
                Require(asset.Unreal.NeedsDestination && retained.Generations.Count == 1, "Deleting an Unreal asset erased local history or left the link writable");
                Require(state.Folders.All(f => f.UnrealPath != "/Game/A"), "Removing the final texture retained an empty parent branch");

                const string destination = "/Game/New/StoneMoved.StoneMoved";
                var otherAsset = state.Assets.Single(a => a.Unreal?.Path == other);
                var moveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                moveTimer.Tick += (_, _) =>
                {
                    var dialog = window.OwnedWindows.Cast<Window>().FirstOrDefault(w => w is UnrealTextureDialog);
                    if (dialog is null) return;
                    moveTimer.Stop();
                    var folder = (ComboBox)dialog.GetType().GetProperty("FolderInput", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                    var name = (TextBox)dialog.GetType().GetProperty("NameInput", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                    var confirm = (Button)dialog.GetType().GetProperty("Confirm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                    folder.Text = "/Game/New";
                    name.Text = "StoneMoved";
                    Require(confirm.IsEnabled, "Unreal move dialog rejected an empty destination");
                    confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                moveTimer.Start();
                try { typeof(MainWindow).GetMethod("MoveUnrealTexture", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [otherAsset]); }
                finally { moveTimer.Stop(); }
                string moveFolder = Directory.EnumerateDirectories(Path.Combine(bridge, "requests")).Single(p =>
                {
                    using var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(p, "request.json")));
                    return request.RootElement.GetProperty("operation").GetString() == "move";
                });
                using (var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(moveFolder, "request.json"))))
                    Require(request.RootElement.GetProperty("path").GetString() == other &&
                        request.RootElement.GetProperty("destination").GetString() == destination &&
                        request.RootElement.GetProperty("revision").GetString() == "rev3",
                        "Unreal move request lost source, destination or conflict revision");
                File.WriteAllText(Path.Combine(moveFolder, "response.json"), JsonSerializer.Serialize(new
                {
                    status = "moved", revision = "rev4"
                }));
                Catalog([destination], revision: "rev4"); Poll(window);
                Require(otherAsset.Unreal?.Path == destination && otherAsset.Name == "StoneMoved" &&
                    state.Assets.Count(a => a.Unreal?.Path == destination) == 1,
                    "Unreal move confirmation duplicated or lost the linked asset");
                string cachedPng = Path.Combine(root, "moved-local.png");
                using (var pixels = new Bitmap(8, 8)) pixels.Save(cachedPng, System.Drawing.Imaging.ImageFormat.Png);
                typeof(MainWindow).GetMethod("LoadUnrealImage", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, [otherAsset, cachedPng, "rev4"]);
                window.OpenWorkspaceAssetAsync(otherAsset).GetAwaiter().GetResult();
                var movedSource = (SourceEntry)typeof(MainWindow).GetField("_activeSource", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                using (var pixels = new Bitmap(4, 4))
                    movedSource.Generations.Add(new ImageLibrary(libraryRoot).SaveGeneration(movedSource, pixels, null, new GenerationEntry { Operation = "Keep after delete" }));

                var deleteTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                deleteTimer.Tick += (_, _) =>
                {
                    var dialog = window.OwnedWindows.Cast<Window>().FirstOrDefault(w => w is TesseraDialog);
                    if (dialog is null) return;
                    deleteTimer.Stop();
                    var buttons = (StackPanel)dialog.FindName("dialogButtons");
                    buttons.Children.OfType<Button>().Single(b => Equals(b.Tag, MessageBoxResult.Yes))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                deleteTimer.Start();
                try { typeof(MainWindow).GetMethod("DeleteUnrealTexture", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [otherAsset]); }
                finally { deleteTimer.Stop(); }
                string deleteFolder = Directory.EnumerateDirectories(Path.Combine(bridge, "requests")).Single(p =>
                {
                    using var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(p, "request.json")));
                    return request.RootElement.GetProperty("operation").GetString() == "delete";
                });
                using (var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(deleteFolder, "request.json"))))
                    Require(request.RootElement.GetProperty("path").GetString() == destination &&
                        request.RootElement.GetProperty("revision").GetString() == "rev4",
                        "Unreal delete request lost the conflict revision");
                File.WriteAllText(Path.Combine(deleteFolder, "response.json"), "{\"status\":\"deleted\"}");
                Catalog([]); Poll(window);
                Require(state.Assets.Contains(otherAsset) && otherAsset.Unreal is { Missing: true, NeedsDestination: true } &&
                    state.Links.All(l => l.AssetId != otherAsset.Id) &&
                    new ImageLibrary(libraryRoot).Load().Single(s => s.Id == otherAsset.Id).Generations.Count == 1,
                    "Unreal deletion did not retain local history or remove the project entry");
            }
            finally { window.Close(); Pump(); }
        }
        finally { Directory.Delete(root, true); }
    }

    private static T Get<T>(FrameworkElement element, string name) => (T)element.FindName(name);
}
