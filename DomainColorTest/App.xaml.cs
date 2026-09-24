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

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private async Task ApplyUpdateAsync(string target, int processId, string digest)
    {
        try { await Task.Run(() => GitHubUpdater.ApplyUpdate(target, processId, digest)); }
        catch (Exception ex) { MessageBox.Show($"Не удалось установить обновление: {ex.Message}", "Tessera", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { Shutdown(); }
    }
}
