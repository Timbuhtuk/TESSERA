using System.Windows;
using System.Windows.Controls;
using DomainColorTest;
using static PixelArtAlignment.Tests.ToolImprovementsChecks;
using Color = System.Drawing.Color;

namespace PixelArtAlignment.Tests;

internal static class ProjectTileChecks
{
    public static void Run()
    {
        string run = Path.Combine(ArtifactRoot, "project-tiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        var library = new ImageLibrary(Path.Combine(run, "library"));
        var sources = new List<SourceEntry>();
        foreach (string name in new[] { "A-nested", "B-only", "Shared", "Unassigned" })
        {
            string path = Path.Combine(run, name + ".png");
            using var image = new Bitmap(13 + sources.Count, 9 + sources.Count);
            using (var draw = Graphics.FromImage(image)) draw.Clear(Color.FromArgb(40 + sources.Count * 30, 100, 180));
            image.Save(path); sources.Add(library.Import(path, sources));
        }
        var catalog = new WorkspaceCatalog(library.DirectoryPath); catalog.SyncImages(sources);
        var projectA = catalog.CreateProject("A"); var projectB = catalog.CreateProject("B");
        var parent = catalog.CreateFolder(projectA.Id, null, "Parent");
        var nested = catalog.CreateFolder(projectA.Id, parent.Id, "Nested");
        catalog.Link(sources[0].Id, projectA.Id, nested.Id);
        catalog.Link(sources[1].Id, projectB.Id, null);
        catalog.Link(sources[2].Id, projectA.Id, null);
        catalog.Link(sources[2].Id, projectB.Id, null);
        catalog.State.View = "project"; catalog.State.ProjectId = projectA.Id;
        catalog.State.FolderId = parent.Id; catalog.Save();
        var window = new MainWindow(library.DirectoryPath) { ShowActivated = false };
        TileInspectorWindow? inspector = null;
        try
        {
            window.EnableWorkspacePrototype(); window.Show(); Pump();
            TileWorkspaceContext Context() => (TileWorkspaceContext)Call(window, "GetTileWorkspaceContext")!;
            void Members(params Guid[] ids) => ToolImprovementsChecks.Require(Context().Materials.Select(item => item.AssetId).Order().SequenceEqual(ids.Order()),
                "Inspector project scope includes foreign/duplicate assets or omits nested/shared images");
            Members(sources[0].Id, sources[2].Id);
            window.Workspace!.State.ProjectId = projectB.Id;
            Members(sources[1].Id, sources[2].Id);
            window.Workspace.State.View = "unassigned";
            Members(sources[3].Id);
            window.Workspace.State.View = "project"; window.Workspace.State.ProjectId = projectA.Id;
            var liveSources = ((System.Collections.IEnumerable)Field(window, "_sources")!).Cast<SourceEntry>().ToArray();
            SourceEntry target = liveSources.Single(item => item.Id == sources[0].Id);
            using var r1 = new Bitmap(5, 4); using var r2 = new Bitmap(7, 6);
            using (var g = Graphics.FromImage(r1)) g.Clear(Color.Red);
            using (var g = Graphics.FromImage(r2)) g.Clear(Color.Green);
            var first = library.SaveGeneration(target, r1, null, new() { Operation = "R1" }); target.Generations.Add(first);
            var second = library.SaveGeneration(target, r2, null, new() { Operation = "R2" }); target.Generations.Add(second);
            target.SelectedGeneration = first;
            inspector = (TileInspectorWindow)Call(window, "GetTileInspector")!; inspector.Show(); Pump();
            Wait(inspector.AddWorkspaceMaterialAsync(target.Id, true));
            ToolImprovementsChecks.Require(inspector.ActiveAsset?.Width == 5 && inspector.ActiveAsset.Height == 4, "Inspector did not load selected R1 result");
            TileAsset loadedFirst = inspector.ActiveAsset!;
            target.SelectedGeneration = second;
            inspector.RefreshWorkspaceMaterials();
            ToolImprovementsChecks.Require(((TileWorkspaceMaterial)Get<ListBox>(inspector, "workspaceMaterialList").SelectedItem).ResultId == second.Id,
                "Inspector did not refresh the current result metadata");
            Call(window, "SaveTileWorkspaceResult", loadedFirst);
            ToolImprovementsChecks.Require(target.Generations.Last().ParentGenerationId == first.Id && target.Generations.Last().Width == 5,
                "Applying loaded R1 attached to current R2 or to the wrong source asset");
            Wait(inspector.AddWorkspaceMaterialAsync(target.Id, false));
            TileAsset original = inspector.ActiveAsset!;
            ToolImprovementsChecks.Require(original.Width == 13 && original.Height == 9, "Source material used result pixels");
            int before = target.Generations.Count;
            var button = (Button)Field(inspector, "_tileWorkspaceApplyButton")!;
            ToolImprovementsChecks.Require((bool)Field(inspector, "_projectDirty")!, "Adding material should make collection unsaved");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            ToolImprovementsChecks.Require(target.Generations.Count == before + 1 && target.Generations.Last().ParentGenerationId is null,
                "Applying original tile did not create an independent source-based history result");
            ToolImprovementsChecks.Require((bool)Field(inspector, "_projectDirty")!, "Applying one tile discarded the collection's unsaved state");
            window.Workspace.State.Links.RemoveAll(link => link.AssetId == target.Id && link.ProjectId == projectA.Id);
            inspector.RefreshWorkspaceMaterials();
            ToolImprovementsChecks.Require(!button.IsEnabled, "Unlinked asset remains applicable to image history");
            string collection = Path.Combine(run, "roundtrip.ttiles");
            inspector.SaveCollectionToFile(collection);
            var loaded = TileCollectionFile.Load(collection);
            ToolImprovementsChecks.Require(loaded.Assets.Count >= 2 && loaded.Assets.Any(asset => asset.SourcePath == original.SourcePath),
                "Project asset collection roundtrip lost material provenance");
            Capture(inspector, Path.Combine(run, "project-a-inspector.png"));
        }
        finally { inspector?.DiscardAndClose(); window.Close(); Pump(); }
    }
}
