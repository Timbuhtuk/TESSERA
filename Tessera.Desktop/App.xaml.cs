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

        try
        {
            string library = e.Args is ["--workspace-library", var requestedLibrary]
                ? Path.GetFullPath(requestedLibrary)
                : ImageLibrary.DefaultDirectory;

            Localization.Initialize(AppearanceSettings.Load(Path.Combine(library, "appearance.json")).Language);
            var prototype = new MainWindow(library, startInWorkspace: true);
            prototype.EnableWorkspacePrototype(enableUpdates: true);
            prototype.EnableReleaseNotes();
            MainWindow = prototype;
            MainWindow.Show();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var settings = AppearanceSettings.Load(AppearanceSettings.DefaultPath);
            Localization.Initialize(settings.Language);
            TesseraDialogs.Show(Localization.Format("TesseraCouldNotPrepareTheLibraryFormat", ex.Message),
                "Tessera", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
    }

    private async Task ApplyUpdateAsync(string target, int processId, string digest)
    {
        try { await Task.Run(() => GitHubUpdater.ApplyUpdate(target, processId, digest)); }
        catch (Exception ex) { TesseraDialogs.Show(Localization.Format("CouldNotInstallUpdateFormat", ex.Message), "Tessera", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { Shutdown(); }
    }
}
