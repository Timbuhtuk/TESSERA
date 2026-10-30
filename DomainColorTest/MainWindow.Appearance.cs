using System.IO;
using System.Windows;
using Tessera.WpfStyles.Themes;

namespace DomainColorTest;

public partial class MainWindow
{
    private readonly string _appearancePath;
    private AppearanceSettings _appearanceSettings;
    private AppearanceWindow? _appearanceWindow;

    private void OpenAppearance(object sender, RoutedEventArgs e)
    {
        if (_appearanceWindow is not null)
        {
            _appearanceWindow.Activate();
            return;
        }

        _appearanceWindow = new AppearanceWindow(_appearanceSettings.Theme, ApplyAppearance, _appearanceSettings.Language, ApplyLanguage,
            _appearanceSettings.HistoryUndoDepth, ApplyUndoDepth) { Owner = this };
        _appearanceWindow.SetUpdateButton(updateButton);
        _appearanceWindow.Closed += (_, _) => _appearanceWindow = null;
        _appearanceWindow.Show();
    }

    private bool ApplyAppearance(TesseraTheme theme)
    {
        var settings = new AppearanceSettings { Theme = theme, Language = _appearanceSettings.Language, HistoryUndoDepth = _appearanceSettings.HistoryUndoDepth };
        try
        {
            settings.Save(_appearancePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TesseraDialog.Show(_appearanceWindow ?? (Window)this,
                Localization.Text("Не удалось сохранить тему. ") + e.Message, Localization.Text("Внешний вид"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        AppearanceResources.Apply(theme);
        _appearanceSettings = settings;
        return true;
    }

    private bool ApplyLanguage(string language)
    {
        var settings = new AppearanceSettings { Theme = _appearanceSettings.Theme, Language = language, HistoryUndoDepth = _appearanceSettings.HistoryUndoDepth };
        try { settings.Save(_appearancePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TesseraDialog.Show(_appearanceWindow ?? (Window)this, Localization.Text("Не удалось сохранить язык. ") + e.Message, Localization.Text("Настройки"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _appearanceSettings = settings;
        return true;
    }

    private bool ApplyUndoDepth(int depth)
    {
        var settings = new AppearanceSettings { Theme = _appearanceSettings.Theme, Language = _appearanceSettings.Language, HistoryUndoDepth = depth };
        try { settings.Save(_appearancePath); _historyUndo.SetCapacity(depth); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TesseraDialog.Show(_appearanceWindow ?? (Window)this, Localization.Text("Не удалось сохранить размер буфера отмены. ") + ex.Message,
                Localization.Text("Настройки"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _appearanceSettings = settings;
        return true;
    }
}
