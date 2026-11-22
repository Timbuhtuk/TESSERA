using System.Windows;

namespace Tessera.Desktop;

public partial class IconWorkspace
{
    public bool CanExport => iconSaveButton.IsEnabled;
    public Task ExportDialogAsync() => SaveDialogAsync();
    public void UseWorkspaceExport()
    {
        iconOpenButton.Visibility = Visibility.Collapsed;
    }
}
