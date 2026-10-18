using System.Windows;

namespace DomainColorTest;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args is ["--apply-update", var target, var processId, var digest] && int.TryParse(processId, out int pid))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = ApplyUpdateAsync(target, pid, digest);
            return;
        }

        if (e.Args.Length >= 1 && e.Args[0] == "--tiles")
        {
            var inspector = new TileInspectorWindow();
            MainWindow = inspector;
            inspector.Show();
            if (e.Args.Length >= 2) _ = inspector.LoadFileAsync(e.Args[1]);
            return;
        }

        string prototypeLibrary = e.Args is ["--workspace-library", var requestedLibrary] ? System.IO.Path.GetFullPath(requestedLibrary) : WorkspacePrototypeLocation.Prepare();
        var prototype = new MainWindow(prototypeLibrary);
        prototype.EnableWorkspacePrototype();
        MainWindow = prototype;
        MainWindow.Show();
    }

    private async Task ApplyUpdateAsync(string target, int processId, string digest)
    {
        try { await Task.Run(() => GitHubUpdater.ApplyUpdate(target, processId, digest)); }
        catch (Exception ex) { TesseraDialog.Show($"Не удалось установить обновление: {ex.Message}", "Tessera", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { Shutdown(); }
    }
}
