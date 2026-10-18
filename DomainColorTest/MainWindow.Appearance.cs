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

        _appearanceWindow = new AppearanceWindow(_appearanceSettings.Theme, ApplyAppearance) { Owner = this };
        _appearanceWindow.Closed += (_, _) => _appearanceWindow = null;
        _appearanceWindow.Show();
    }

    private bool ApplyAppearance(TesseraTheme theme)
    {
        var settings = new AppearanceSettings { Theme = theme };
        try
        {
            settings.Save(_appearancePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TesseraDialog.Show(_appearanceWindow ?? (Window)this,
                "Не удалось сохранить тему. " + e.Message, "Внешний вид", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        AppearanceResources.Apply(theme);
        _appearanceSettings = settings;
        return true;
    }
}
