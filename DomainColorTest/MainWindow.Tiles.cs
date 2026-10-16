using System.Windows;

namespace DomainColorTest;

public partial class MainWindow
{
    private TileInspectorWindow? _tileInspector;

    private void OpenTileInspector(object sender, RoutedEventArgs e)
    {
        if (_tileInspector is not null)
        {
            if (_tileInspector.WindowState == WindowState.Minimized) _tileInspector.WindowState = WindowState.Normal;
            _tileInspector.Activate();
            return;
        }
        _tileInspector = new TileInspectorWindow { Owner = this };
        _tileInspector.Closed += (_, _) => _tileInspector = null;
        _tileInspector.Show();
    }
}
