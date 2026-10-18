using System.Windows;

namespace DomainColorTest;

public partial class TileInspectorWindow
{
    public void AddTileSetAssets(IReadOnlyList<TileAsset> assets)
    {
        if (_busy) throw new InvalidOperationException(Localization.Text("Дождитесь сглаживания."));
        FinishStroke();
        if (assets.Count == 0) return;
        foreach (var asset in assets) _assets.Add(asset);
        assetList.SelectedItem = assets[0];
        RefreshActiveAsset(); SelectCompanion(null); ConfigureWatchers(); SetProjectDirty();
        statusLabel.Text = Localization.Format("Тайлсет добавлен · {0} отдельных тайлов", assets.Count);
    }

    private void OpenTileSetClicked(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        FinishStroke();
        var importer = new TileSetImportWindow { Owner = this };
        if (importer.ShowDialog() == true) AddTileSetAssets(importer.ImportedAssets);
    }
}
