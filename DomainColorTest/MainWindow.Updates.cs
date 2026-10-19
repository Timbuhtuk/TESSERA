using System.Diagnostics;
using System.ComponentModel;
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
        checkUpdatesMenuItem.Click += async (_, _) =>
        {
            CloseNavigationMenus();
            await CheckForUpdatesAsync(true);
        };
        Loaded += async (_, _) => { if (_autoCheckUpdates) await CheckForUpdatesAsync(false); };
    }

    private async Task CheckForUpdatesAsync(bool showResult)
    {
        if (_checkingUpdates) return;
        _checkingUpdates = true;
        updateButton.IsEnabled = checkUpdatesMenuItem.IsEnabled = false;
        updateButton.Content = Localization.Text("Проверка…");
        try
        {
            _availableRelease = await GitHubUpdater.CheckAsync();
            updateButton.Content = UpdateButtonText();
            if (showResult && _availableRelease is null)
                TesseraDialog.Show(this, Localization.Format("Установлена актуальная версия ({0}).", GitHubUpdater.CurrentVersion.ToString(3)), "Tessera");
        }
        catch (Exception ex)
        {
            updateButton.Content = UpdateButtonText();
            if (showResult) TesseraDialog.Show(this, Localization.Format("Не удалось проверить обновления: {0}", ex.Message), "Tessera", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _checkingUpdates = false;
            updateButton.IsEnabled = checkUpdatesMenuItem.IsEnabled = true;
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
        if (_processing || iconWorkspace.IsBusy || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy || moodboardWorkspace.IsBusy)
        {
            TesseraDialog.Show(this, Localization.Text("Дождитесь завершения обработки перед обновлением."), "Tessera");
            return;
        }
        var choice = TesseraDialog.Show(this,
            Localization.Format("Скачать Tessera {0} и перезапустить приложение? История изображений сохранится.", release.Tag),
            Localization.Text("Обновление Tessera"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (choice != MessageBoxResult.Yes) return;

        updateButton.IsEnabled = checkUpdatesMenuItem.IsEnabled = false;
        updateButton.Content = Localization.Text("Загрузка…");
        try
        {
            string downloaded = await GitHubUpdater.DownloadAsync(release);
            if (_processing || iconWorkspace.IsBusy || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy || moodboardWorkspace.IsBusy)
                throw new InvalidOperationException(Localization.Text("Дождитесь завершения обработки и нажмите обновление снова."));
            if (!TryCloseForUpdate(() => GitHubUpdater.StartInstall(downloaded, release.Sha256)))
                statusLabel.Text = Localization.Text("Обновление отменено: приложение осталось открытым.");
        }
        catch (Exception ex)
        {
            TesseraDialog.Show(this, Localization.Format("Не удалось обновить Tessera: {0}", ex.Message), "Tessera", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            updateButton.Content = UpdateButtonText();
            updateButton.IsEnabled = checkUpdatesMenuItem.IsEnabled = true;
        }
    }

    private string UpdateButtonText() => _availableRelease is null ? Localization.Text("Проверить обновления") :
        GitHubUpdater.CanInstall ? Localization.Format("Обновить до {0}", _availableRelease.Tag) : Localization.Format("Открыть релиз {0}", _availableRelease.Tag);

    private bool TryCloseForUpdate(Action startInstall)
    {
        bool started = false;
        Exception? error = null;
        CancelEventHandler prepare = (_, e) =>
        {
            if (e.Cancel) return;
            try { startInstall(); started = true; }
            catch (Exception ex) { error = ex; e.Cancel = true; }
        };
        Closing += prepare;
        try { Close(); }
        finally { Closing -= prepare; }
        if (error is not null) throw new InvalidOperationException(error.Message, error);
        return started;
    }
}
