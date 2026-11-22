using System.Windows;
using System.Windows.Controls;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

public partial class BackgroundWorkspace
{
    public bool CanExport => backgroundSaveButton.IsEnabled;
    public Task ExportDialogAsync() => SaveDialogAsync();
    private Button? _workspaceApplyButton;
    private Func<Bitmap, Task>? _workspaceApply;
    public void EnableWorkspaceApply(Func<Bitmap, Task> apply)
    {
        _workspaceApply = apply;
        backgroundOpenButton.Visibility = Visibility.Collapsed;
        backgroundEditorChoices.Visibility = Visibility.Collapsed;
        if (_workspaceApplyButton is null)
        {
            _workspaceApplyButton = new Button { Content = Localization.Text("AddToAssetHistory"), Margin = new Thickness(6, 0, 0, 0) };
            ((StackPanel)backgroundSaveButton.Parent).Children.Add(_workspaceApplyButton);
            _workspaceApplyButton.Click += async (_, _) =>
            {
                if (_result is null || IsRendering || _busy || _workspaceApply is not { } applyToAsset) return;
                try { await applyToAsset((Bitmap)_result.Clone()); }
                catch (Exception ex) { TesseraDialog.Show(Window.GetWindow(this), ex.Message, "Tessera"); }
            };
        }
        _workspaceApplyButton.Visibility = Visibility.Visible;
        UpdateSaveState();
    }
}
