using System.IO;

namespace DomainColorTest;

public static class LibraryLocation
{
    public static string? MigrationWarning { get; private set; }

    public static string PrepareDefault()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        MigrationWarning = null;
        try { return Prepare(appData); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MigrationWarning = ex.Message;
            return Path.Combine(appData, "Tessera", "Library");
        }
    }

    public static string Prepare(string appData)
    {
        string tessera = Path.Combine(Path.GetFullPath(appData), "Tessera");
        string library = Path.Combine(tessera, "Library");
        string legacy = Path.Combine(Path.GetFullPath(appData), "Pixelizator", "Library");
        string marker = Path.Combine(tessera, ".pixelizator-library-migrated");

        if (File.Exists(marker) || !Directory.Exists(legacy)) return library;
        if (IsLink(legacy) || Directory.Exists(tessera) && IsLink(tessera) || Directory.Exists(library) && IsLink(library))
            throw new IOException("Папка библиотеки перенаправлена. Перенос истории остановлен.");

        Directory.CreateDirectory(library);
        foreach (string source in Directory.EnumerateDirectories(legacy))
        {
            string id = Path.GetFileName(source);
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            if (IsLink(source)) continue;

            string target = Path.Combine(library, id);
            Directory.CreateDirectory(target);
            CopyIfMissing(Path.Combine(source, "source.png"), Path.Combine(target, "source.png"));

            string generations = Path.Combine(source, "generations");
            if (Directory.Exists(generations) && !IsLink(generations))
            {
                foreach (string result in Directory.EnumerateDirectories(generations))
                {
                    string resultId = Path.GetFileName(result);
                    if (!Guid.TryParseExact(resultId, "N", out _) || IsLink(result)) continue;
                    string resultTarget = Path.Combine(target, "generations", resultId);
                    Directory.CreateDirectory(resultTarget);
                    CopyIfMissing(Path.Combine(result, "result.png"), Path.Combine(resultTarget, "result.png"));
                    CopyIfMissing(Path.Combine(result, "crop.png"), Path.Combine(resultTarget, "crop.png"));
                    CopyIfMissing(Path.Combine(result, "generation.json"), Path.Combine(resultTarget, "generation.json"));
                }
            }

            CopyIfMissing(Path.Combine(source, "source.json"), Path.Combine(target, "source.json"));
        }

        File.WriteAllText(marker, "Legacy Pixelizator library copied to Tessera. The original remains untouched.");
        return library;
    }

    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static void CopyIfMissing(string source, string target)
    {
        if (!File.Exists(source) || File.Exists(target) || IsLink(source)) return;
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporary);
            if (!File.Exists(target)) File.Move(temporary, target);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
