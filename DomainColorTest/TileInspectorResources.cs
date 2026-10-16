using System.Windows;

namespace DomainColorTest;

public sealed class TileInspectorResources : ResourceDictionary
{
    public TileInspectorResources()
    {
        // Reuse the running application's theme, including live changes. Standalone UI checks
        // have no Application instance and load the same checked-in theme directly.
        MergedDictionaries.Add(Application.Current?.Resources ?? new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/DomainColorTest;component/Theme.xaml")
        });
    }
}
