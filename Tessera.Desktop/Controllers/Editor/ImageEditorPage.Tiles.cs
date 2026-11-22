using System.Windows;
using System.Windows.Data;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

public partial class ImageEditorPage
{
    internal TileAtlasPage Atlas { get; private set; } = null!;

    internal SourceEntry? ActiveSource => _activeSource;

    private void InitializeAtlas()
    {
        Atlas = new TileAtlasPage(this);
        atlasFrame.Content = Atlas;
        atlasFrame.SetBinding(VisibilityProperty, new Binding(nameof(Visibility)) { Source = Atlas.atlasPreviewHost });
        Atlas.InitializeSettings();
        settingsPanel.Children.Add(Atlas.SettingsGroup);
    }

    internal void OpenAtlasTool()
        => Dock.OpenEditorTool(TileAtlasPage.ToolKey, Localization.Text("TileAtlas"), 620, Atlas.SettingsGroup);

    internal void ActivateAtlasTool(bool active)
    {
        Atlas.SetActive(active);
        UpdateAtlasView();
    }

    internal void UpdateAtlasView()
    {
        bool visible = Atlas.IsActive && ImageEditorVisible;
        Preview.ShowAtlas(visible);
        Atlas.UpdateView(visible);
    }

    internal bool CommitAtlasEdit(SourceEntry source, Bitmap result, Guid? parent)
    {
        string operation = Localization.Text("AtlasEdit");
        var entry = Host._library.SaveGeneration(source, result, null, new GenerationEntry
        {
            Operation = operation, Caption = operation, OperationId = "manual.tile", ParentGenerationId = parent,
            RecipeUnavailableReason = Localization.Text("TheHistoryIncludesAManualAtlas"),
            PreservesTransparency = true, Settings = Tools.CaptureSettings()
        });
        return History.CommitGeneration(source, entry);
    }
}
