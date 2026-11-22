using System.IO;
using System.Windows;

namespace Tessera.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args is ["--apply-update", var target, var processId, var digest] && int.TryParse(processId, out int pid))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Localization.Initialize(AppearanceSettings.Load(AppearanceSettings.DefaultPath).Language);
            _ = ApplyUpdateAsync(target, pid, digest);
            return;
        }

        string library;
        try
        {
            library = e.Args is ["--workspace-library", var requestedLibrary]
                ? Path.GetFullPath(requestedLibrary)
                : PortableLibraryMigration.Prepare(confirmReplacement: ConfirmPortableLibraryReplacement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var settings = AppearanceSettings.Load(Path.Combine(AppContext.BaseDirectory, "WorkspaceData", "Library", "appearance.json"));
            Localization.Initialize(settings.Language);
            TesseraDialog.Show(Localization.Format("TesseraCouldNotPrepareTheLibraryFormat", ex.Message),
                "Tessera", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        Localization.Initialize(AppearanceSettings.Load(Path.Combine(library, "appearance.json")).Language);
        var prototype = new MainWindow(library, startInWorkspace: true);
        prototype.EnableWorkspacePrototype(enableUpdates: true);
        prototype.EnableReleaseNotes();
        MainWindow = prototype;
        MainWindow.Show();
        // TESSERA_LEGACY Retain the standalone tile inspector's --tiles startup option for the atlas and archive extraction.
        if (e.Args.Length >= 2 && e.Args[0] == "--tiles")
            _ = prototype.OpenAtlasFromCommandLineAsync(e.Args[1]);
    }

    private static bool ConfirmPortableLibraryReplacement(string source)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var settings = AppearanceSettings.Load(Path.Combine(profile, "Tessera", "Library", "appearance.json"));
        Localization.Initialize(settings.Language);
        return TesseraDialog.Show(Localization.Format("ConfirmPortableLibraryReplacementFormat", source),
            "Tessera", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private async Task ApplyUpdateAsync(string target, int processId, string digest)
    {
        try { await Task.Run(() => GitHubUpdater.ApplyUpdate(target, processId, digest)); }
        catch (Exception ex) { TesseraDialog.Show(Localization.Format("CouldNotInstallUpdateFormat", ex.Message), "Tessera", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { Shutdown(); }
    }
}
