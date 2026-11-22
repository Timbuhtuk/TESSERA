using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Tessera.Desktop;

namespace Tessera.RegressionTests;

internal static class ResourceLifetimeChecks
{
    public static void Verify(string root)
    {
        string folder = Path.Combine(root, "resource-lifetimes");
        Directory.CreateDirectory(folder);
        string input = Path.Combine(folder, "image.png");
        using var image = new Bitmap(1024, 1024);
        image.SetPixel(0, 0, Color.CornflowerBlue);
        image.Save(input);
        VerifyCache(folder, input);

        string libraryPath = Path.Combine(folder, "library");
        var library = new ImageLibrary(libraryPath);
        var catalog = new WorkspaceCatalog(libraryPath);
        var firstProject = catalog.CreateProject("First");
        var secondProject = catalog.CreateProject("Second");
        var sources = new List<SourceEntry>();
        using var result = new Bitmap(64, 64);
        for (int q = 0; q <= 50; q++)
        {
            string path = Path.Combine(folder, "source-" + q + ".png");
            File.Copy(input, path);
            var source = library.Import(path, sources);
            var generation = library.SaveGeneration(source, result, null, new GenerationEntry());
            source.Generations.Add(generation);
            source.SelectedGeneration = generation;
            sources.Add(source);
        }
        catalog.SyncImages(sources);
        foreach (var source in sources)
        {
            catalog.State.Assets.Single(a => a.Id == source.Id).SelectedResult = source.SelectedGeneration!.Id;
            catalog.State.Links.Add(new WorkspaceLink { AssetId = source.Id, ProjectId = source == sources[^1] ? secondProject.Id : firstProject.Id });
        }
        catalog.State.View = "project";
        catalog.State.ProjectId = firstProject.Id;
        catalog.Save();

        var window = new MainWindow(libraryPath, startInWorkspace: true) { ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.EnableWorkspacePrototype();
            window.Show(); Pump();
            var headers = Get<IEnumerable<SourceEntry>>(window, "_sources");
            var resources = Get<ImageLibrary>(window, "_library").Resources;
            var assets = window.Workspace!.State.Assets;
            var first = headers.Single(source => source.Id == sources[0].Id);
            var next = headers.Single(source => source.Id == sources[^1].Id);
            Require(headers.Count() == 51 && headers.All(source => !source.HasLoadedHistory), "Workspace startup eagerly loaded result models.");
            Require(!resources.Contains(library.SourcePath(next)), "Startup decoded the inactive project's image.");

            Wait(window.OpenWorkspaceAssetAsync(assets.Single(asset => asset.Id == first.Id)));
            var previousSource = Get<Bitmap>(window, "_sourceImage");
            var previousResult = Get<Bitmap>(window, "_downscaledResult");
            string previousPath = library.SourcePath(first);
            Require(first.HasLoadedHistory && headers.Count(source => source.HasLoadedHistory) == 1, "Opening one image loaded unrelated histories.");
            Require(resources.Get(previousPath, 112, 112) is not null && resources.Contains(previousPath), "Requested thumbnail was not cached.");
            var undo = Get<HistoryUndoStore>(window, "_historyUndo");
            undo.Begin(first, new BranchPresentation()).Commit();
            var openMaps = typeof(MainWindow).GetMethod("OpenMaterialMapsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Wait((Task)openMaps.Invoke(window, null)!);
            var materialMaps = window.OwnedWindows.OfType<MaterialMapsWindow>().Single();
            Require(materialMaps.Document is not null, "Fixture did not open a material map document.");

            window.Browser!.Navigate("project", secondProject.Id); Pump();
            Require(window.Workspace.State.ProjectId == secondProject.Id && window.Browser.IsVisible &&
                Get<Bitmap?>(window, "_sourceImage") is null && Get<Bitmap?>(window, "_downscaledResult") is null,
                "Project switch retained the previous editor.");
            Require(IsDisposed(previousSource) && IsDisposed(previousResult) &&
                !sources.Take(50).Any(source => resources.Contains(library.SourcePath(source)) ||
                    resources.Contains(library.ResultPath(source, source.SelectedResultId!.Value))) &&
                headers.All(source => !source.HasLoadedHistory),
                "Project switch retained decoded pixels or history objects.");
            Require(materialMaps.Document is null && !window.OwnedWindows.OfType<MaterialMapsWindow>().Any(),
                "Project switch retained a material map document from the previous project.");

            Wait(window.OpenWorkspaceAssetAsync(assets.Single(asset => asset.Id == next.Id)));
            Require(next.HasLoadedHistory && headers.Count(source => source.HasLoadedHistory) == 1,
                "The new project opened multiple histories.");
            var restore = typeof(EditorHistory).GetMethod("RestoreHistorySource", BindingFlags.Instance | BindingFlags.NonPublic)!;
            restore.Invoke(window.Editor.History, [undo.Undo() ?? throw new InvalidOperationException("Fixture did not record undo.")]);
            Require(!first.HasLoadedHistory && ReferenceEquals(Get<SourceEntry>(window, "_activeSource"), next),
                "Undo reopened an inactive project.");
            var removeResult = typeof(EditorHistory).GetMethod("RemoveGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!;
            removeResult.Invoke(window.Editor.History, [next.SelectedGeneration]);
            Require(headers.Count(source => source.HasLoadedHistory) == 1,
                "Editing the new project loaded histories from the previous one.");
            window.Browser.Navigate("project", firstProject.Id); Pump();
            Wait(window.OpenWorkspaceAssetAsync(assets.Single(asset => asset.Id == first.Id)));
            Require(first.HasLoadedHistory && !next.HasLoadedHistory && first.SelectedGeneration?.Id == sources[0].SelectedResultId,
                "Returning to the first project did not restore the saved history selection.");
            Console.WriteLine("  Resource scopes: 51 source headers, 0 histories at startup, 1 per open document, 0 after switching away.");
        }
        finally { window.Close(); Pump(); }
    }

    private static void VerifyCache(string folder, string input)
    {
        string preview = Path.Combine(folder, "thumbnails");
        Directory.CreateDirectory(preview);
        var cache = new ImageResources();
        string first = Path.Combine(preview, "0.png");
        for (int q = 0; q <= ImageResources.MaxEntries; q++)
        {
            string copy = Path.Combine(preview, q + ".png");
            File.Copy(input, copy);
            var loaded = cache.Get(copy, 112, 112);
            Require(loaded is { PixelWidth: 112, PixelHeight: 112 } && ReferenceEquals(loaded, cache.Get(copy, 112, 112)),
                "Cache did not reuse a scaled thumbnail.");
        }
        var usage = cache.Usage;
        Require(usage.Count <= ImageResources.MaxEntries && usage.Bytes <= ImageResources.MaxBytes && !cache.Contains(first), "Thumbnail cache exceeded its capacity.");
        Console.WriteLine($"  Cache after 257 requests: {usage.Count} thumbnails, {usage.Bytes:N0} estimated pixel bytes.");
        cache.Clear();
        Require(cache.Usage == (0, 0), "Scope change did not empty the thumbnail cache.");
    }

    private static bool IsDisposed(Bitmap bitmap)
    {
        try { _ = bitmap.Width; return false; }
        catch (ArgumentException) { return true; }
    }

    private static T Get<T>(MainWindow window, string field)
        => (T)WindowTestAccess.GetField(window, field, BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static void Wait(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new TimeoutException("Editor did not finish opening the image.");
        task.GetAwaiter().GetResult();
        Pump();
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
