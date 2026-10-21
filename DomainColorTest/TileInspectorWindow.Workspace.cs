using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace DomainColorTest;

public sealed record TileWorkspaceMaterial(Guid AssetId, string Name, string Location, string SourcePath,
    string? ResultPath, Guid? ResultId, BitmapSource? Thumbnail)
{
    public string Label => string.IsNullOrEmpty(Location) ? Name : Name + " · " + Location;
}

public sealed record TileWorkspaceContext(string Key, string Label, IReadOnlyList<TileWorkspaceMaterial> Materials);

public partial class TileInspectorWindow
{
    private Func<TileWorkspaceContext>? _workspaceMaterials;
    private TileWorkspaceContext? _workspaceContext;
    private bool _refreshingWorkspace, _loadingWorkspaceMaterial;

    public void SetWorkspaceMaterials(Func<TileWorkspaceContext> materials)
    {
        _workspaceMaterials = materials;
        workspaceSourceButton.Content = Localization.Text("Исходник");
        workspaceResultButton.Content = Localization.Text("Результат");
        workspaceSourceButton.ToolTip = Localization.Text("Добавить копию исходника в коллекцию тайлов. Исходный файл останется без изменений.");
        workspaceResultButton.ToolTip = Localization.Text("Добавить выбранный в истории результат этого ассета в коллекцию тайлов.");
        workspaceMaterialsHint.Text = Localization.Text("Выберите ассет, затем добавьте исходник или его выбранный результат. Правки сохраняются в историю отдельной кнопкой.");
        AutomationProperties.SetName(workspaceMaterialList, Localization.Text("Изображения текущего проекта"));
        AutomationProperties.SetName(workspaceSourceButton, Localization.Text("Добавить исходник в коллекцию тайлов"));
        AutomationProperties.SetName(workspaceResultButton, Localization.Text("Добавить выбранный результат в коллекцию тайлов"));
        RefreshWorkspaceMaterials();
        Activated += (_, _) => RefreshWorkspaceMaterials();
    }

    public bool RefreshWorkspaceMaterials()
    {
        if (_workspaceMaterials is null || _refreshingWorkspace || _closed) return false;
        _refreshingWorkspace = true;
        try
        {
            var context = _workspaceMaterials();
            if (_workspaceContext is not null && _workspaceContext.Key != context.Key)
            {
                if (_busy || _loadingWorkspaceMaterial) { _loadVersion++; return false; }
                if (!ConfirmReplaceCollection()) return false;
                FinishStroke(); _loadVersion++;
                _reloadTimer.Stop(); _changedPaths.Clear();
                _settingCollection = true;
                try { _assets.Clear(); assetList.SelectedItem = null; }
                finally { _settingCollection = false; }
                _historyAsset = null; _projectPath = WorkspaceSavePath = null; _projectDirty = false;
                RefreshActiveAsset(); ConfigureWatchers(); UpdateProjectTitle();
            }
            Guid? selected = (workspaceMaterialList.SelectedItem as TileWorkspaceMaterial)?.AssetId;
            _workspaceContext = context;
            workspaceMaterialsPanel.Visibility = Visibility.Visible;
            workspaceMaterialsTitle.Text = Localization.Format("Изображения · {0}", context.Label);
            workspaceMaterialList.ItemsSource = context.Materials;
            workspaceMaterialList.SelectedItem = context.Materials.FirstOrDefault(item => item.AssetId == selected) ?? context.Materials.FirstOrDefault();
            UpdateWorkspaceMaterialControls();
            return true;
        }
        catch (Exception ex) { statusLabel.Text = Localization.Text("Не удалось обновить ассеты проекта: ") + ex.Message; return false; }
        finally { _refreshingWorkspace = false; }
    }

    public async Task AddWorkspaceMaterialAsync(Guid assetId, bool result)
    {
        if (_busy || _loadingWorkspaceMaterial || !RefreshWorkspaceMaterials()) return;
        var material = _workspaceContext!.Materials.FirstOrDefault(item => item.AssetId == assetId);
        if (material is null) return;
        string? path = result ? material.ResultPath : material.SourcePath;
        if (path is null) return;
        workspaceMaterialList.SelectedItem = material;
        _loadingWorkspaceMaterial = true;
        UpdateWorkspaceMaterialControls();
        try
        {
            string label = material.Name + " · " + (result ? Localization.Text("Результат") : Localization.Text("Исходник"));
            await LoadFileAsync(path, displayName: label);
        }
        finally { _loadingWorkspaceMaterial = false; RefreshWorkspaceMaterials(); UpdateWorkspaceMaterialControls(); }
    }

    public bool OpenWorkspaceCollection(string path)
    {
        if (_busy || _loadingWorkspaceMaterial || !RefreshWorkspaceMaterials()) return false;
        if (string.Equals(_projectPath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            WorkspaceSavePath = path;
            return true;
        }
        if (!ConfirmReplaceCollection()) return false;
        LoadCollectionFromFile(path); WorkspaceSavePath = path;
        return true;
    }

    private void UpdateWorkspaceMaterialControls()
    {
        var selected = workspaceMaterialList.SelectedItem as TileWorkspaceMaterial;
        workspaceMaterialList.IsEnabled = !_busy && !_loadingWorkspaceMaterial;
        workspaceSourceButton.IsEnabled = !_busy && !_loadingWorkspaceMaterial && selected is not null;
        workspaceResultButton.IsEnabled = !_busy && !_loadingWorkspaceMaterial && selected?.ResultPath is not null;
        workspaceResultButton.ToolTip = selected?.ResultPath is null
            ? Localization.Text("У этого ассета ещё нет выбранного результата в истории.")
            : Localization.Text("Добавить выбранный в истории результат этого ассета в коллекцию тайлов.");
        UpdateWorkspaceApplyControls();
    }

    private void WorkspaceMaterialSelected(object sender, SelectionChangedEventArgs e) => UpdateWorkspaceMaterialControls();
    private async void AddWorkspaceSourceClicked(object sender, RoutedEventArgs e)
    {
        if (workspaceMaterialList.SelectedItem is TileWorkspaceMaterial material) await AddWorkspaceMaterialAsync(material.AssetId, false);
    }
    private async void AddWorkspaceResultClicked(object sender, RoutedEventArgs e)
    {
        if (workspaceMaterialList.SelectedItem is TileWorkspaceMaterial material) await AddWorkspaceMaterialAsync(material.AssetId, true);
    }
}
