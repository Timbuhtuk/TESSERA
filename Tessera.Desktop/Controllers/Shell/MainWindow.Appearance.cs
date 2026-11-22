using System.IO;
using System.Windows;
using System.Windows.Input;
using Tessera.WpfStyles.Themes;

namespace Tessera.Desktop;

public partial class MainWindow
{
    private readonly string _appearancePath;

    internal AppearanceSettings _appearanceSettings;

    private AppearanceWindow? _appearanceWindow;

    private void OpenAppearance(object sender, RoutedEventArgs e)
    {
        if (_appearanceWindow is not null)
        {
            _appearanceWindow.Activate();
            return;
        }

        _appearanceWindow = new AppearanceWindow(_appearanceSettings.Theme, ApplyAppearance, _appearanceSettings.Language, ApplyLanguage,
            _appearanceSettings.HistoryUndoDepth, ApplyUndoDepth, _appearanceSettings.ComparisonPreviewKey, ApplyComparisonKey) { Owner = this };
        _appearanceWindow.ConfigureNotifications(_appearanceSettings.NotificationAnchor, ApplyNotificationPosition);
        _appearanceWindow.AddLensShortcuts(_appearanceSettings, ApplyLensKey);
        _appearanceWindow.SetUpdateButton(updateButton);
        _appearanceWindow.UninstallRequested += UninstallTessera;
        _appearanceWindow.Closed += (_, _) => _appearanceWindow = null;
        _appearanceWindow.Show();
    }

    private bool ApplyNotificationPosition(NotificationPlacement position)
    {
        var settings = _appearanceSettings.Copy();
        settings.NotificationPosition = position.ToString();
        try { settings.Save(_appearancePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TesseraDialog.Show(_appearanceWindow ?? (Window)this, Localization.Text("CouldNotSaveTheNotificationPosition") + ex.Message,
                Localization.Text("Settings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _appearanceSettings = settings;
        notificationDisplay.Place(position);
        return true;
    }

    private bool ApplyAppearance(TesseraTheme theme)
    {
        var settings = _appearanceSettings.Copy();
        settings.Theme = theme;
        try
        {
            settings.Save(_appearancePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TesseraDialog.Show(_appearanceWindow ?? (Window)this,
                Localization.Text("CouldNotSaveTheme") + e.Message, Localization.Text("Appearance"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        AppearanceResources.Apply(theme);
        _appearanceSettings = settings;
        return true;
    }

    private bool ApplyLanguage(string language)
    {
        var settings = _appearanceSettings.Copy();
        settings.Language = language;
        try { settings.Save(_appearancePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TesseraDialog.Show(_appearanceWindow ?? (Window)this, Localization.Text("CouldNotSaveLanguage") + e.Message, Localization.Text("Settings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _appearanceSettings = settings;
        return true;
    }

    private bool ApplyUndoDepth(int depth)
    {
        var settings = _appearanceSettings.Copy();
        settings.HistoryUndoDepth = depth;
        try { settings.Save(_appearancePath); Editor.History.SetUndoCapacity(depth); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TesseraDialog.Show(_appearanceWindow ?? (Window)this, Localization.Text("UndoBufferSizeSaveFailed") + ex.Message,
                Localization.Text("Settings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _appearanceSettings = settings;
        return true;
    }

    private bool ApplyComparisonKey(Key key)
    {
        var settings = _appearanceSettings.Copy();
        settings.ComparisonKey = key.ToString();
        try { settings.Save(_appearancePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TesseraDialog.Show(_appearanceWindow ?? (Window)this, Localization.Text("CouldNotSaveTheAlternativePreview") + ex.Message,
                Localization.Text("Settings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        Editor.Preview.ResetComparisonKey();
        _appearanceSettings = settings;
        Editor.Preview.UpdateComparisonKeyHints();
        return true;
    }

    private AppearanceSettings? ApplyLensKey(string tool, Key key)
    {
        var settings = _appearanceSettings.Copy();
        settings.LensKeys = new(settings.LensKeys);
        var other = AppearanceSettings.Lenses.FirstOrDefault(lens => lens.Tool != tool && settings.LensKey(lens.Tool) == key);
        if (other.Tool is not null)
            settings.LensKeys[other.Tool] = settings.LensKey(tool).ToString();
        settings.LensKeys[tool] = key.ToString();
        try { settings.Save(_appearancePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TesseraDialog.Show(_appearanceWindow ?? (Window)this, Localization.Text("CouldNotSaveTheLensShortcut") + ex.Message,
                Localization.Text("Settings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        _appearanceSettings = settings;
        Editor.Preview.UpdateLensKeyHints();
        return settings;
    }
}
