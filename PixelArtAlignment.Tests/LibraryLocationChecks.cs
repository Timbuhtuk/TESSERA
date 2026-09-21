using DomainColorTest;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;

namespace PixelArtAlignment.Tests;

internal static class LibraryLocationChecks
{
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "migration-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string input = Path.Combine(root, "original.png");
        using var image = new Bitmap(2, 2);
        image.SetPixel(0, 0, Color.Red);
        image.SetPixel(1, 1, Color.Blue);
        image.Save(input);

        var legacy = new ImageLibrary(Path.Combine(root, "Pixelizator", "Library"));
        var oldSource = legacy.Import(input, []);
        var result = legacy.SaveGeneration(oldSource, image, null, new GenerationEntry { Caption = "Первый результат" });
        oldSource.Generations.Add(result);
        var promoted = legacy.ImportGeneration(oldSource, result, [oldSource]);

        string otherInput = Path.Combine(root, "new.png");
        image.Save(otherInput);
        var current = new ImageLibrary(Path.Combine(root, "Tessera", "Library"));
        var newSource = current.Import(otherInput, []);

        string path = LibraryLocation.Prepare(root);
        Require(path == current.DirectoryPath, "New application directory was not selected");
        var loaded = current.Load();
        Require(loaded.Count == 3 && loaded.Any(s => s.Id == newSource.Id), "Migration replaced existing Tessera records");
        var restored = loaded.Single(s => s.Id == oldSource.Id);
        Require(restored.Generations.Count == 1 && restored.Generations[0].Id == result.Id, "Generation history was not copied");
        Require(loaded.Any(s => s.Id == promoted.Id), "Independent source made from a result was not copied");
        using (var copied = ImageLibrary.ReadBitmap(current.ResultPath(restored, restored.Generations[0])))
            Require(copied.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb(), "Migrated pixels changed");
        Require(File.Exists(legacy.SourcePath(oldSource)) && File.Exists(legacy.ResultPath(oldSource, result)), "Legacy history was modified");

        current.RemoveSource(restored);
        LibraryLocation.Prepare(root);
        Require(current.Load().Count == 2, "Deleted source returned from legacy library after restart");
        Require(File.Exists(Path.Combine(root, "Tessera", ".pixelizator-library-migrated")), "Migration marker is missing");
        Console.WriteLine("  Tessera library: old history and existing new records merged, originals preserved, deletion not resurrected.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
