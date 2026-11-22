using System.Windows;

namespace Tessera.Desktop;

public sealed class MaterialMapsResources : ResourceDictionary
{
    public MaterialMapsResources()
    {
        // Reuse the running application's theme, including live changes. Standalone UI checks
        // have no Application instance and load the same checked-in theme directly.
        MergedDictionaries.Add(Application.Current?.Resources ?? new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Tessera.Desktop;component/Views/Resources/Theme.xaml")
        });
    }
}
