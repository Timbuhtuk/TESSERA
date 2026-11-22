namespace Tessera.Desktop;

public partial class AsepriteWorkspace
{
    public bool CanExport => sheetSaveButton.IsEnabled;
    public Task ExportDialogAsync() => SaveDialogAsync(false);
    public Func<IReadOnlyList<string>, Task>? WorkspaceImport { get; set; }
    public string? CurrentSheetPath => (sheetStrip.SelectedItem as SheetEntry)?.Result?.ImagePath;
    public void ApplyWorkspaceSettings(WorkspaceAsset asset)
    {
        sheetLayout.SelectedIndex = Math.Clamp(asset.SheetLayout, 0, 2); sheetColumns.Value = asset.SheetColumns; sheetPadding.Value = asset.SheetPadding;
    }
    public void CaptureWorkspaceSettings(WorkspaceAsset asset)
    {
        asset.SheetLayout = sheetLayout.SelectedIndex; asset.SheetColumns = sheetColumns.Value; asset.SheetPadding = sheetPadding.Value;
    }
}
