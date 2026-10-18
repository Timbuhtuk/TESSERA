using System.Windows;
using Tessera.WpfStyles.Themes;

namespace DomainColorTest;

public sealed class AppearanceResources : ResourceDictionary
{
    [ThreadStatic] private static ResourceDictionary? _shared;

    public static ResourceDictionary Shared => _shared ??= new ResourceDictionary
    {
        Source = new Uri("pack://application:,,,/DomainColorTest;component/Theme.xaml")
    };

    public AppearanceResources() => MergedDictionaries.Add(Shared);

    public static void Apply(TesseraTheme theme) => ThemeManager.Apply(theme, Shared);
}
