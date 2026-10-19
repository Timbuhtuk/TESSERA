using System.Windows;

namespace DomainColorTest;

public partial class MainWindow
{
    private TileInspectorWindow? _tileInspector;
    private readonly HashSet<string> _closedTileSourcePaths = new(StringComparer.OrdinalIgnoreCase);

    private void OpenTileInspector(object sender, RoutedEventArgs e)
    {
        if (_tileInspector is not null)
        {
            if (_tileInspector.WindowState == WindowState.Minimized) _tileInspector.WindowState = WindowState.Normal;
            _tileInspector.Activate();
            return;
        }
        _tileInspector = new TileInspectorWindow { Owner = this, ValidateExportPath = EnsureExportPathAllowed };
        _tileInspector.Closed += (_, _) =>
        {
            foreach (string source in _tileInspector.SourcePaths) _closedTileSourcePaths.Add(source);
            _tileInspector = null;
        };
        _tileInspector.Show();
    }
}
