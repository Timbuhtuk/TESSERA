using System.Diagnostics;
using System.Windows;

namespace DomainColorTest;

public partial class MainWindow
{
    private TesseraRelease? _availableRelease;
    private bool _checkingUpdates;
    private bool _autoCheckUpdates;

    private void InitializeUpdates()
    {
        updateButton.Click += async (_, _) =>
        {
            if (_availableRelease is null) await CheckForUpdatesAsync(true);
            else await InstallUpdateAsync();
        };
        checkUpdatesMenuButton.Click += async (_, _) =>
        {
            fileMenuPopup.IsOpen = false;
            await CheckForUpdatesAsync(true);
        };
        Loaded += async (_, _) => { if (_autoCheckUpdates) await CheckForUpdatesAsync(false); };
    }

    private async Task CheckForUpdatesAsync(bool showResult)
    {
        if (_checkingUpdates) return;
        _checkingUpdates = true;
        updateButton.IsEnabled = checkUpdatesMenuButton.IsEnabled = false;
        updateButton.Content = "Проверка…";
        try
        {
            _availableRelease = await GitHubUpdater.CheckAsync();
            updateButton.Content = UpdateButtonText();
            if (showResult && _availableRelease is null)
                MessageBox.Show(this, $"Установлена актуальная версия ({GitHubUpdater.CurrentVersion.ToString(3)}).", "Tessera");
        }
        catch (Exception ex)
        {
            updateButton.Content = UpdateButtonText();
            if (showResult) MessageBox.Show(this, $"Не удалось проверить обновления: {ex.Message}", "Tessera", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _checkingUpdates = false;
            updateButton.IsEnabled = checkUpdatesMenuButton.IsEnabled = true;
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_availableRelease is not { } release) return;
        if (!GitHubUpdater.CanInstall)
        {
            Process.Start(new ProcessStartInfo(release.Page.AbsoluteUri) { UseShellExecute = true });
            return;
        }
        if (_processing || iconWorkspace.IsBusy || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy)
        {
            MessageBox.Show(this, "Дождитесь завершения обработки перед обновлением.", "Tessera");
            return;
        }
        var choice = MessageBox.Show(this,
            $"Скачать Tessera {release.Tag} и перезапустить приложение? История изображений сохранится.",
            "Обновление Tessera", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (choice != MessageBoxResult.Yes) return;

        updateButton.IsEnabled = checkUpdatesMenuButton.IsEnabled = false;
        updateButton.Content = "Загрузка…";
        try
        {
            string downloaded = await GitHubUpdater.DownloadAsync(release);
            if (_processing || iconWorkspace.IsBusy || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy)
                throw new InvalidOperationException("Дождитесь завершения обработки и нажмите обновление снова.");
            GitHubUpdater.StartInstall(downloaded, release.Sha256);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не удалось обновить Tessera: {ex.Message}", "Tessera", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            updateButton.Content = UpdateButtonText();
            updateButton.IsEnabled = checkUpdatesMenuButton.IsEnabled = true;
        }
    }

    private string UpdateButtonText() => _availableRelease is null ? "Проверить обновления" :
        GitHubUpdater.CanInstall ? $"Обновить до {_availableRelease.Tag}" : $"Открыть релиз {_availableRelease.Tag}";
}
