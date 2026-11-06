using System.Windows;
using Tessera.WpfStyles.Themes;

namespace Tessera.Desktop;

public sealed class AppearanceResources : ResourceDictionary
{
    [ThreadStatic] private static ResourceDictionary? _shared;

    public static ResourceDictionary Shared => _shared ??= new ResourceDictionary
    {
        Source = new Uri("pack://application:,,,/Tessera.Desktop;component/Theme.xaml")
    };

    public AppearanceResources() => MergedDictionaries.Add(Shared);

    public static void Apply(TesseraTheme theme) => ThemeManager.Apply(theme, Shared);
}
