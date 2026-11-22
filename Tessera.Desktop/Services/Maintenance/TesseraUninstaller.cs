using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tessera.Desktop;

internal static class TesseraUninstaller
{
    private const string ScriptResource = "Tessera.Uninstall.ps1";
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    internal sealed record Plan(string Executable, string[] Directories, string[] Files, string? EmptyPortableDirectory)
    {
        internal IEnumerable<string> Targets => Directories.Concat(Files).Append(Executable);
    }

    internal static Plan Prepare(string activeLibrary)
    {
        if (!GitHubUpdater.CanInstall || Environment.ProcessPath is not { } executable)
            throw new InvalidOperationException(Localization.Text("UninstallAvailableSingleFileTesseraBuild"));
        return Prepare(executable, activeLibrary, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.GetTempPath(), Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR"),
            AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string);
    }

    internal static Plan Prepare(string executable, string activeLibrary, string localData, string temp,
        string? bundleBase = null, string? nativeSearchPaths = null)
    {
        if (!Path.IsPathFullyQualified(executable) || !Path.IsPathFullyQualified(localData) || !Path.IsPathFullyQualified(temp))
            throw new InvalidOperationException(Localization.Text("CouldNotLocateTheTesseraFolders"));
        executable = Path.GetFullPath(executable);
        string appDirectory = Path.GetDirectoryName(executable)!;
        CheckPath(executable);
        if (!File.Exists(executable) || !IsTesseraExecutable(executable))
            throw new InvalidOperationException(Localization.Text("CouldNotLocateTheTesseraExecutable"));

        var directories = new HashSet<string>(PathComparer);
        var files = new HashSet<string>(PathComparer);
        AddDirectory(Path.Combine(localData, "Tessera"));
        AddDirectory(Path.Combine(temp, "Tessera"));

        string defaultBundleBase = Path.GetFullPath(Path.Combine(temp, ".net"));
        string extractBase = bundleBase is not null && Path.IsPathFullyQualified(bundleBase)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(bundleBase)) : defaultBundleBase;
        foreach (string root in new[] { defaultBundleBase, extractBase }.Distinct(PathComparer))
        {
            AddDirectory(Path.Combine(root, "Tessera"));
            AddDirectory(Path.Combine(root, "Tessera.Desktop"));
        }
        // A renamed executable gets a separate bundle cache. Use only the actual bundle directory.
        string cacheParent = Path.Combine(extractBase, Path.GetFileNameWithoutExtension(executable));
        foreach (string entry in (nativeSearchPaths ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Path.IsPathFullyQualified(entry)) continue;
            string cache = Path.TrimEndingDirectorySeparator(Path.GetFullPath(entry));
            if (PathComparer.Equals(Path.GetDirectoryName(cache), cacheParent)) AddDirectory(cache);
        }

        string portableDirectory = Path.Combine(appDirectory, "WorkspaceData");
        string portableLibrary = Path.Combine(portableDirectory, "Library");
        if (Directory.Exists(portableLibrary))
        {
            CheckPath(portableLibrary);
            if (PathComparer.Equals(portableLibrary, Path.TrimEndingDirectorySeparator(Path.GetFullPath(activeLibrary))) || IsTesseraLibrary(portableLibrary))
                AddDirectory(portableLibrary);
        }

        CheckPath(temp);
        foreach (string path in Directory.EnumerateFiles(temp, "tessera-map-*.png"))
            if (Regex.IsMatch(Path.GetFileName(path), @"\Atessera-map-[0-9a-f]{32}\.png\z", RegexOptions.IgnoreCase)) AddFile(path);
        foreach (string path in Directory.EnumerateFiles(appDirectory, ".Tessera-*.exe"))
            if (Regex.IsMatch(Path.GetFileName(path), @"\A\.Tessera-(?:backup|update)-[0-9a-f]{32}\.exe\z", RegexOptions.IgnoreCase) &&
                IsTesseraExecutable(path)) AddFile(path);

        string[] roots = directories.Where(path => !directories.Any(other => !PathComparer.Equals(path, other) && IsInside(path, other))).ToArray();
        return new Plan(executable, roots, files.Where(path => !roots.Any(root => IsInside(path, root))).ToArray(),
            directories.Contains(portableLibrary) ? portableDirectory : null);

        void AddDirectory(string candidate)
        {
            string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            if (!Directory.Exists(path)) return;
            CheckPath(path);
            directories.Add(path);
        }

        void AddFile(string path)
        {
            if (PathComparer.Equals(path, executable)) return;
            CheckPath(path);
            files.Add(path);
        }
    }

    private static bool IsTesseraLibrary(string path) =>
        File.Exists(Path.Combine(path, "workspace.json")) && (File.Exists(Path.Combine(path, "release-notes-state.json")) ||
            File.Exists(Path.Combine(path, "appearance.json"))) ||
        Directory.EnumerateDirectories(path).Any(directory => Guid.TryParseExact(Path.GetFileName(directory), "N", out _) &&
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0 &&
            File.Exists(Path.Combine(directory, "source.json")) && File.Exists(Path.Combine(directory, "source.png")));

    private static bool IsInside(string path, string directory) => path.StartsWith(
        Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsTesseraExecutable(string path) => Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(FileVersionInfo.GetVersionInfo(path).ProductName, "Tessera", StringComparison.OrdinalIgnoreCase);

    private static void CheckPath(string path)
    {
        for (string? current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(Localization.Format("UninstallStoppedBecauseAPathIsFormat", current));
        }
    }

    internal static ProcessStartInfo CreateStartInfo(Plan plan, Process owner, bool showErrorDialog = true)
    {
        string system = Environment.SystemDirectory;
        var start = new ProcessStartInfo(Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = system
        };
        using var stream = typeof(TesseraUninstaller).Assembly.GetManifestResourceStream(ScriptResource)!;
        using var script = new StreamReader(stream);
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ReadToEnd())) })
            start.ArgumentList.Add(argument);

        var executable = new FileInfo(plan.Executable);
        start.Environment["TESSERA_UNINSTALL_PLAN"] = JsonSerializer.Serialize(new
        {
            plan.Executable, plan.Directories, plan.Files, plan.EmptyPortableDirectory,
            OwnerId = owner.Id, OwnerStarted = owner.StartTime.ToUniversalTime().Ticks,
            executable.Length, LastWrite = executable.LastWriteTimeUtc.Ticks, ShowErrorDialog = showErrorDialog,
            CleansSharedProfile = plan.Directories.Contains(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tessera"), PathComparer),
            ErrorTitle = Localization.Text("UninstallTesseraLabel"), ErrorMessage = Localization.Text("CouldNotFinishUninstallingTessera"),
            CloseOtherInstances = Localization.Text("CloseOtherTesseraInstancesRetryUninstall"),
            CloseTimeout = Localization.Text("TesseraDidNotCloseWithin30"),
            ChangedExecutable = Localization.Text("TheExecutableChangedAfterConfirmation"),
            ReadOnlyExecutable = Localization.Text("TheExecutableIsReadOnly"),
            RedirectedPath = Localization.Text("RedirectedPathPrefix")
        });
        return start;
    }
}
