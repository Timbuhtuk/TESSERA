using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DomainColorTest;
using PixelArtDownscale;
using static PixelArtAlignment.Tests.ToolImprovementsChecks;
using Color = System.Drawing.Color;

namespace PixelArtAlignment.Tests;

internal static class BackgroundHistoryChecks
{
    public static void Run()
    {
        string run = Path.Combine(ArtifactRoot, "bg-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        string path = Path.Combine(run, "original.png");
        using var original = Fixture(13, 9, Color.Blue);
        using var firstPixels = Fixture(5, 4, Color.Red);
        using var secondPixels = Fixture(7, 6, Color.Lime);
        original.Save(path);
        var library = new ImageLibrary(Path.Combine(run, "library"));
        var imported = library.Import(path, []);
        var window = new MainWindow(library.DirectoryPath) { ShowActivated = false };
        BackgroundWorkspace? workspace = null;
        var audit = new List<object>();
        try
        {
            window.EnableWorkspacePrototype(); window.Show(); Pump();
            var sources = ((System.Collections.IEnumerable)Field(window, "_sources")!).Cast<SourceEntry>();
            SourceEntry target = sources.Single(source => source.Id == imported.Id);
            WorkspaceAsset asset = window.Workspace!.State.Assets.Single(item => item.Id == target.Id);
            Wait(window.OpenWorkspaceAssetAsync(asset));
            GenerationEntry first = library.SaveGeneration(target, firstPixels, null, Entry("R1", 5, 4));
            GenerationEntry second = library.SaveGeneration(target, secondPixels, null, Entry("R2", 7, 6));
            target.Generations.Add(first); target.Generations.Add(second);
            string sourceHash = Hash(library.SourcePath(target));
            string firstHash = Hash(library.ResultPath(target, first));
            string secondHash = Hash(library.ResultPath(target, second));

            Verify(0, original, null);
            Verify(1, firstPixels, first.Id);

            ToolImprovementsChecks.Require(Hash(library.SourcePath(target)) == sourceHash && Hash(library.ResultPath(target, first)) == firstHash &&
                Hash(library.ResultPath(target, second)) == secondHash, "Background history apply overwrote an immutable input snapshot");
            ToolImprovementsChecks.Require(Pixels((Bitmap)Field(window, "_sourceImage")!).SequenceEqual(Pixels(original)), "Background history apply changed original editor pixels");
            File.WriteAllText(Path.Combine(run, "history-provenance.json"), JsonSerializer.Serialize(new
            {
                sourceHash, firstHash, secondHash, sourceUnchanged = true, cases = audit
            }, new JsonSerializerOptions { WriteIndented = true }));

            void Verify(int basis, Bitmap expectedInput, Guid? expectedParent)
            {
                Call(window, "SelectGeneration", first);
                window.ProcessingBasis = basis;
                Get<CheckBox>(window, "aspectLock").IsChecked = false;
                int loadedWidth = 31 + basis, loadedHeight = 21 + basis;
                Get<IntegerInput>(window, "widthInput").Value = loadedWidth;
                Get<IntegerInput>(window, "heightInput").Value = loadedHeight;
                Wait((Task)Call(window, "WorkspaceActionAsync", "background", asset)!);
                workspace = Get<BackgroundWorkspace>(window, "backgroundWorkspace");
                var loaded = (Bitmap)Field(workspace, "_source")!;
                ToolImprovementsChecks.Require(loaded.Width == expectedInput.Width && loaded.Height == expectedInput.Height &&
                    Pixels(loaded).SequenceEqual(Pixels(expectedInput)), "Background workspace loaded pixels from the wrong processing basis");
                workspace.SetCustomBackgroundColor(Color.Magenta);
                Get<Slider>(workspace, "backgroundToleranceInput").Value = 0;
                Wait(workspace.RefreshPreviewAsync());
                var processed = (Bitmap)Field(workspace, "_result")!;
                ToolImprovementsChecks.Require(Pixels(processed).SequenceEqual(Pixels(expectedInput)), "An absent exact background color unexpectedly changed the fixture");
                var apply = (Func<Bitmap, Task>)Field(workspace, "_workspaceApply")!;

                Call(window, "SelectGeneration", second);
                window.ProcessingBasis = 1 - basis;
                Get<IntegerInput>(window, "widthInput").Value = 49;
                Get<IntegerInput>(window, "heightInput").Value = 33;
                ToolImprovementsChecks.Require(target.SelectedGeneration?.Id == second.Id, "Test did not change current selection after loading background input");
                int count = target.Generations.Count;
                Wait(apply((Bitmap)processed.Clone()));
                ToolImprovementsChecks.Require(target.Generations.Count == count + 1, "Registered background apply callback did not save history");
                GenerationEntry saved = target.Generations.Last();
                ToolImprovementsChecks.Require(saved.ParentGenerationId == expectedParent, basis == 0
                    ? "Original-basis background attached to the later selected result instead of explicit null parent"
                    : "Result-basis background attached to the later selected result instead of the result loaded at open");
                ToolImprovementsChecks.Require(saved.Width == expectedInput.Width && saved.Height == expectedInput.Height, "Background history saved current-selection dimensions instead of loaded input");
                ToolImprovementsChecks.Require(saved.Settings.Downscale.TargetWidth == loadedWidth && saved.Settings.Downscale.TargetHeight == loadedHeight,
                    "Background history recorded settings changed after the workspace input was loaded");
                using var output = ImageLibrary.ReadBitmap(library.ResultPath(target, saved));
                ToolImprovementsChecks.Require(Pixels(output).SequenceEqual(Pixels(expectedInput)), "Background history saved current selection or mutated alpha instead of the workspace result");
                string metadataPath = Path.Combine(Path.GetDirectoryName(library.ResultPath(target, saved))!, "generation.json");
                using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
                JsonElement parent = metadata.RootElement.GetProperty(nameof(GenerationEntry.ParentGenerationId));
                ToolImprovementsChecks.Require(expectedParent.HasValue ? parent.GetGuid() == expectedParent : parent.ValueKind == JsonValueKind.Null,
                    "Persisted background history parent differs from its in-memory provenance");
                audit.Add(new
                {
                    basis, expectedParent, actualParent = saved.ParentGenerationId, selectedAfterLoad = second.Id,
                    saved.Id, saved.Width, saved.Height, loadedWidth, loadedHeight, pixelsAndAlphaExact = true
                });
            }
        }
        catch (Exception exception) { throw new Exception("Background history check: " + exception, exception); }
        finally { workspace?.Dispose(); window.Close(); Pump(); }
    }

    private static Bitmap Fixture(int width, int height, Color color)
    {
        var image = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var draw = Graphics.FromImage(image)) draw.Clear(color);
        image.SetPixel(1, 1, Color.FromArgb(127, color));
        image.SetPixel(0, 0, Color.FromArgb(0, 0, 0, 0));
        return image;
    }

    private static GenerationEntry Entry(string name, int width, int height) => new()
    {
        Operation = name, Settings = new() { Downscale = new() { TargetWidth = width, TargetHeight = height, Palette = PaletteKind.None } }
    };

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
