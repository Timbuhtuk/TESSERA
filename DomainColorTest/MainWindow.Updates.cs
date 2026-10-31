using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DomainColorTest;

public partial class MainWindow
{
    private TesseraRelease? _availableRelease;
    private bool _checkingUpdates;
    private bool _autoCheckUpdates;
    private bool _installingUpdate, _updatesClosed;
    private readonly HashSet<string> _notifiedReleaseTags = [];
    private readonly DispatcherTimer _updateCheckTimer = new() { Interval = TimeSpan.FromHours(6) };
    private readonly DispatcherTimer _updateOfferTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    internal Func<Task<TesseraRelease?>> ReleaseCheckAsync { get; set; } = () => GitHubUpdater.CheckAsync();

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
        Loaded += async (_, _) =>
        {
            if (!_autoCheckUpdates) return;
            _updateCheckTimer.Start();
            await CheckForUpdatesAsync(false);
        };
        _updateCheckTimer.Tick += async (_, _) => { if (_autoCheckUpdates) await CheckForUpdatesAsync(false); };
        _updateOfferTimer.Tick += async (_, _) => await OfferUpdateWhenReadyAsync();
        Closed += (_, _) => { _updatesClosed = true; _updateCheckTimer.Stop(); _updateOfferTimer.Stop(); };
    }

    private async Task CheckForUpdatesAsync(bool showResult)
    {
        if (_checkingUpdates || _installingUpdate || _updatesClosed) return;
        _checkingUpdates = true;
        updateButton.IsEnabled = checkUpdatesMenuItem.IsEnabled = false;
        updateButton.Content = Localization.Text("Проверка…");
        try
        {
            _availableRelease = await ReleaseCheckAsync();
            if (_updatesClosed) return;
            updateButton.Content = UpdateButtonText();
            if (showResult && _availableRelease is null)
                TesseraDialog.Show(this, Localization.Format("Установлена актуальная версия ({0}).", GitHubUpdater.CurrentVersion.ToString(3)), "Tessera");
        }
        catch (Exception ex)
        {
            _mainProgress?.Dispose();
            if (_updatesClosed) return;
            updateButton.Content = UpdateButtonText();
            if (showResult) TesseraDialog.Show(this, Localization.Format("Не удалось проверить обновления: {0}", ex.Message), "Tessera", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        finally
        {
            _checkingUpdates = false;
            updateButton.IsEnabled = checkUpdatesMenuItem.IsEnabled = true;
        }
        if (_updatesClosed || _availableRelease is null) { _updateOfferTimer.Stop(); return; }
        if (showResult) await InstallUpdateAsync();
        else if (!_notifiedReleaseTags.Contains(_availableRelease.Tag)) _updateOfferTimer.Start();
    }

    private async Task OfferUpdateWhenReadyAsync()
    {
        if (_updatesClosed || _availableRelease is null || _notifiedReleaseTags.Contains(_availableRelease.Tag))
        { _updateOfferTimer.Stop(); return; }
        if (_checkingUpdates || _installingUpdate || WorkspaceBusy || !IsVisible || !IsActive || !IsEnabled || WindowState == WindowState.Minimized ||
            OwnedWindows.Cast<Window>().Any(w => w.IsVisible) || Mouse.Captured is not null || mainMenu.Items.OfType<MenuItem>().Any(m => m.IsSubmenuOpen)) return;
        _updateOfferTimer.Stop();
        await InstallUpdateAsync();
    }

    private async Task InstallUpdateAsync()
    {
        if (_installingUpdate || _updatesClosed || _availableRelease is not { } release) return;
        if (WorkspaceBusy)
        {
            TesseraDialog.Show(this, Localization.Text("Дождитесь завершения обработки перед обновлением."), "Tessera");
            return;
        }
        _installingUpdate = true;
        try
        {
            _notifiedReleaseTags.Add(release.Tag);
            string message = GitHubUpdater.CanInstall
                ? Localization.Format("Доступна Tessera {0}. Скачать обновление и перезапустить приложение? История изображений сохранится.", release.Tag)
                : Localization.Format("Доступна Tessera {0}. Открыть страницу релиза для скачивания?", release.Tag);
            var choice = TesseraDialog.Show(this, message, Localization.Text("Обновление Tessera"), MessageBoxButton.YesNo, MessageBoxImage.Question,
                Localization.Text(GitHubUpdater.CanInstall ? "Обновить" : "Открыть релиз"), Localization.Text("Позже"));
            if (choice != MessageBoxResult.Yes || _updatesClosed) return;
            if (!GitHubUpdater.CanInstall)
            {
                Process.Start(new ProcessStartInfo(release.Page.AbsoluteUri) { UseShellExecute = true });
                return;
            }
            updateButton.IsEnabled = checkUpdatesMenuItem.IsEnabled = false;
            updateButton.Content = Localization.Text("Загрузка…");
            string downloaded = await GitHubUpdater.DownloadAsync(release);
            if (_updatesClosed) return;
            if (WorkspaceBusy)
                throw new InvalidOperationException(Localization.Text("Дождитесь завершения обработки и нажмите обновление снова."));
            if (!TryCloseForUpdate(() => GitHubUpdater.StartInstall(downloaded, release.Sha256)))
                statusLabel.Text = Localization.Text("Обновление отменено: приложение осталось открытым.");
        }
        catch (Exception ex)
        {
            _mainProgress?.Dispose();
            if (!_updatesClosed) TesseraDialog.Show(this, Localization.Format("Не удалось обновить Tessera: {0}", ex.Message), "Tessera", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _installingUpdate = false;
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
