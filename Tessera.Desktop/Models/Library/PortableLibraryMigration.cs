using System.IO;
using System.Security.Cryptography;

namespace Tessera.Desktop;

// TESSERA_LEGACY Move pre-AppData WorkspaceData/Library installs to the user profile and resume interrupted copies.
public static class PortableLibraryMigration
{
    private const string CompleteMarker = ".portable-library-migrated";
    private const string Verification = "Portable library copy verified with SHA-256.";

    public static string Prepare(string? applicationDirectory = null, string? appDataDirectory = null)
    {
        string appData = appDataDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appData)) throw new IOException(Localization.Text("CurrentUserSLocalAppdataFolder"));

        string profile = Path.Combine(Path.GetFullPath(appData), "Tessera");
        string library = Path.Combine(profile, "Library");
        string staging = Path.Combine(profile, ".portable-library-ready");
        string backup = Path.Combine(profile, "Library.before-portable-migration");
        string portableData = Path.Combine(Path.GetFullPath(applicationDirectory ?? AppContext.BaseDirectory), "WorkspaceData");
        string portable = Path.Combine(portableData, "Library");

        Directory.CreateDirectory(profile);
        IsPlainDirectory(profile);
        string lockPath = Path.Combine(profile, ".portable-library-migration.lock");
        PlainAttributes(lockPath);
        using var migrationLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        bool existingLibrary = IsPlainDirectory(library);
        if (existingLibrary && IsVerified(library)) return library;

        if (IsPlainDirectory(staging))
        {
            if (!IsVerified(staging))
                throw new IOException(Localization.Format("CouldNotVerifyTheStagedLibraryFormat", staging));
        }
        else
        {
            bool hasPortableData = IsPlainDirectory(portableData) && IsPlainDirectory(portable) &&
                Directory.EnumerateFileSystemEntries(portable).Any(path => Path.GetFileName(path) != ".copied-library");
            if (!hasPortableData)
            {
                if (!existingLibrary && IsPlainDirectory(backup)) Directory.Move(backup, library);
                return LibraryLocation.Prepare(appData);
            }

            if (Overlaps(portable, library) || Overlaps(portable, staging) || Overlaps(portable, backup))
                throw new IOException(Localization.Text("TheLibraryMigrationFoldersOverlap"));

            string working = Path.Combine(profile, ".portable-library-copy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(working);
            try
            {
                CopyTree(portable, working);
                VerifyTree(portable, working);
                using (var marker = new FileStream(Path.Combine(working, CompleteMarker), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    marker.Write(System.Text.Encoding.UTF8.GetBytes(Verification));
                    marker.Flush(flushToDisk: true);
                }
                Directory.Move(working, staging);
            }
            finally
            {
                if (IsPlainDirectory(working))
                {
                    CheckTree(working);
                    Directory.Delete(working, recursive: true);
                }
            }
        }

        // The directory already contains its completion marker when it becomes the active library.
        if (existingLibrary) Directory.Move(library, backup);
        try { Directory.Move(staging, library); }
        catch
        {
            if (existingLibrary && !Directory.Exists(library)) Directory.Move(backup, library);
            throw;
        }
        return library;
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string path in Directory.EnumerateFileSystemEntries(source))
        {
            string target = Path.Combine(destination, Path.GetFileName(path));
            if (IsPlainDirectory(path)) { CopyTree(path, target); continue; }
            using var input = File.OpenRead(path);
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(path));
        }
    }

    private static void VerifyTree(string source, string destination)
    {
        string[] entries = Directory.GetFileSystemEntries(source);
        if (entries.Length != Directory.GetFileSystemEntries(destination).Length)
            throw new IOException(Localization.Format("TheFolderContentsChangedDuringMigrationFormat", source));
        foreach (string path in entries)
        {
            string copy = Path.Combine(destination, Path.GetFileName(path));
            if (IsPlainDirectory(path))
            {
                if (!IsPlainDirectory(copy)) throw new DirectoryNotFoundException(copy);
                VerifyTree(path, copy);
                continue;
            }
            PlainAttributes(copy);
            using var input = File.OpenRead(path);
            using var output = File.OpenRead(copy);
            if (input.Length != output.Length || !SHA256.HashData(input).AsSpan().SequenceEqual(SHA256.HashData(output)))
                throw new IOException(Localization.Format("TheLibraryFileChecksumDidNotFormat", path));
        }
    }

    private static void CheckTree(string directory)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            if (IsPlainDirectory(path)) CheckTree(path);
    }

    private static bool IsPlainDirectory(string path)
    {
        var attributes = PlainAttributes(path);
        if (attributes is null) return false;
        return (attributes & FileAttributes.Directory) != 0;
    }

    private static bool IsVerified(string directory)
    {
        string path = Path.Combine(directory, CompleteMarker);
        return PlainAttributes(path) is { } attributes && (attributes & FileAttributes.Directory) == 0 &&
            File.ReadAllText(path) == Verification;
    }

    private static FileAttributes? PlainAttributes(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException(Localization.Format("LibraryMigrationStoppedBecausePathRedirectedFormat", path));
        return attributes;
    }

    private static bool Overlaps(string first, string second) =>
        IsInside(first, second) || IsInside(second, first);

    private static bool IsInside(string path, string directory) =>
        (Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar)
            .StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
