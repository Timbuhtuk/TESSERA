using System.IO.Compression;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DomainColorTest;

namespace PixelArtAlignment.Tests;

internal static class WorkspaceExportChecks
{
    public static void Run()
    {
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "workspace-exports-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string first = Path.Combine(folder, "first.png"), second = Path.Combine(folder, "second.png"), third = Path.Combine(folder, "third.png");
        using var image = new Bitmap(8, 8);
        using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.White);
        image.Save(first); image.SetPixel(3, 3, Color.Red); image.Save(second); image.SetPixel(4, 4, Color.Blue); image.Save(third);
        var originals = new[] { first, second, third }.ToDictionary(path => path, File.ReadAllBytes);
        var window = new MainWindow(Path.Combine(folder, "library")) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        TileInspectorWindow? tiles = null, reopened = null;
        try
        {
            window.Show(); Pump();
            Invoke(window, "LoadImage", first); Invoke(window, "LoadImage", second);
            var background = (BackgroundWorkspace)window.FindName("backgroundWorkspace");
            background.SetImage(image, "third.png", third); Await(background.RefreshPreviewAsync());
            foreach (string path in new[] { first, second })
            {
                Await(background.SaveAsync(path.ToUpperInvariant(), true));
                using (var saved = new Bitmap(path)) Require(saved.GetPixel(0, 0).A == 0, "External overwrite lost background transparency");
                File.WriteAllBytes(path, originals[path]);
            }
            Reject(() => Await(background.SaveAsync(third, true)));
            string project = Path.Combine(folder, "existing.tboard"); File.WriteAllText(project, "keep project");
            Reject(() => Await(background.SaveAsync(project, true)));
            Require(File.ReadAllText(project) == "keep project", "Background export changed a project file");
            var library = Get<ImageLibrary>(window, "_library");
            Reject(() => Await(background.SaveAsync(Path.Combine(library.DirectoryPath, "new.png"), true)));
            string output = Path.Combine(folder, "background.PNG");
            Await(background.SaveAsync(output)); Await(background.SaveAsync(output, true));
            using (var saved = new Bitmap(output)) Require(saved.GetPixel(0, 0).A == 0, "Safe background export lost transparency");
            // The editor may explicitly replace an external workspace source; its internal copy remains independent.
            ((IntegerInput)window.FindName("widthInput")).Value = 4;
            ((IntegerInput)window.FindName("heightInput")).Value = 4;
            ((Button)window.FindName("scaleOnlyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitUntil(() => !Get<bool>(window, "_processing"));
            Invoke(window, "SaveResult", third);
            VerifyEditorOverwrite(third);
            File.WriteAllBytes(third, originals[third]);

            var icons = (IconWorkspace)window.FindName("iconWorkspace");
            icons.SetImage(image, "third.png", third);
            Reject(() => Await(icons.SaveAsync(Path.Combine(library.DirectoryPath, "new.ico"), true)));
            Reject(() => Await(icons.SaveAsync(Path.Combine(folder, "wrong.png"), true)));
            Await(icons.SaveAsync(Path.Combine(folder, "safe.ico")));

            var board = (MoodboardWorkspace)window.FindName("moodboardWorkspace");
            ((Button)board.FindName("createButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            board.ExportToPng(second);
            using (var saved = new Bitmap(second)) Require(saved.Width == board.WorkspaceWidth && saved.Height == board.WorkspaceHeight, "External board overwrite dimensions differ");
            File.WriteAllBytes(second, originals[second]);
            Reject(() => board.SaveToFile(Path.Combine(library.DirectoryPath, "board.tboard")));
            board.SaveToFile(Path.Combine(folder, "board.tboard"));

            Invoke(window, "OpenTileInspector", window, new RoutedEventArgs()); Pump();
            tiles = Get<TileInspectorWindow>(window, "_tileInspector");
            WaitUntil(() => !Get<bool>(tiles, "_loadingWorkspaceMaterial"));
            // The project-aware inspector now starts with the editor's active material.
            // This export/removal fixture needs an explicit empty collection before adding its pair.
            string emptyCollection = Path.Combine(folder, "empty.ttiles");
            TileCollectionFile.Save(emptyCollection, [], new TileCollectionSettings());
            tiles.LoadCollectionFromFile(emptyCollection);
            string fourth = Path.Combine(folder, "tile-only.png"); image.Save(fourth);
            originals.Add(fourth, File.ReadAllBytes(fourth));
            Await(tiles.LoadFileAsync(first)); Await(tiles.LoadFileAsync(second));
            var removed = tiles.Assets.Single(asset => asset.SourcePath == first);
            tiles.SelectAsset(removed.Id);
            Invoke(tiles, "RemoveAssetClicked", new Button(), new RoutedEventArgs(Button.ClickEvent));
            Require(tiles.Assets.Count == 1 && tiles.ActiveAsset?.SourcePath == second, "Tile removal fixture failed");
            Reject(() => tiles.ExportActivePng(first));
            tiles.ExportActivePng(third);
            using (var saved = new Bitmap(third)) Require(saved.Size == image.Size, "External tile overwrite dimensions differ");
            File.WriteAllBytes(third, originals[third]);
            Reject(() => tiles.ExportActivePng(Path.Combine(library.DirectoryPath, "tile.png")));
            Reject(() => tiles.ExportActivePng(project));
            Reject(() => tiles.SaveCollectionToFile(Path.Combine(folder, "wrong.png")));
            string collection = Path.Combine(folder, "saved.ttiles");
            tiles.SaveCollectionToFile(collection); tiles.SaveCollectionToFile(collection);
            reopened = new TileInspectorWindow { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            reopened.Show(); reopened.LoadCollectionFromFile(collection);
            Reject(() => reopened.ExportActivePng(Path.GetRelativePath(Environment.CurrentDirectory, first).ToUpperInvariant()));
            Reject(() => reopened.ExportActivePng(collection));
            reopened.ExportActivePng(Path.Combine(folder, "safe-tile.png"));
            reopened.SaveCollectionToFile(Path.Combine(folder, "reopened.ttiles"));

            string legacy = Path.Combine(folder, "legacy.ttiles"); File.Copy(collection, legacy);
            using (var archive = ZipFile.Open(legacy, ZipArchiveMode.Update))
            {
                var entry = archive.GetEntry("collection.json")!;
                JsonNode manifest;
                using (var stream = entry.Open()) manifest = JsonNode.Parse(stream)!;
                manifest.AsObject().Remove("SourcePaths"); entry.Delete();
                using var outputStream = archive.CreateEntry("collection.json").Open();
                using var writer = new StreamWriter(outputStream); writer.Write(manifest.ToJsonString());
            }
            reopened.LoadCollectionFromFile(legacy);
            Require(reopened.Assets.Count == 1 && reopened.ActiveAsset!.SourcePath == second, "Legacy tile collection did not open");
            foreach (var pair in originals) Require(File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value), "Export changed original " + pair.Key);
            Require(File.ReadAllText(project) == "keep project", "Export changed unrelated project");
            Require(!Directory.EnumerateFiles(folder, "*.tmp", SearchOption.AllDirectories).Any(), "Rejected export left temporary files");
            Await(tiles.LoadFileAsync(fourth));
            tiles.DiscardAndClose(); tiles = null;
            Invoke(window, "SaveResult", fourth);
            VerifyEditorOverwrite(fourth);
            File.WriteAllBytes(fourth, originals[fourth]);
            Console.WriteLine("  Workspace exports: explicit external overwrite, internal-library/workspace-source protection, correct formats, removed tiles, project reopen/legacy and repeat exports passed.");

            void VerifyEditorOverwrite(string path)
            {
                using var saved = new Bitmap(path);
                var expected = Get<Bitmap>(window, "_downscaledResult");
                Require(saved.Size == expected.Size, "Editor overwrite dimensions differ");
                for (int y = 0; y < saved.Height; y++)
                for (int x = 0; x < saved.Width; x++)
                    Require(saved.GetPixel(x, y) == expected.GetPixel(x, y), "Editor overwrite pixel differs");
            }
        }
        finally { reopened?.DiscardAndClose(); tiles?.DiscardAndClose(); window.Close(); Pump(); }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException or IOException or ArgumentException) { return; }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException) { return; }
        throw new Exception("Protected path or wrong extension accepted");
    }
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static void Await(Task task) { WaitUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); Pump(); }
    private static void WaitUntil(Func<bool> condition)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(20);
        while (!condition() && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        Require(condition(), "Workspace export timed out"); Pump();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
