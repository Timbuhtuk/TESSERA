using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Tessera.Desktop;

public partial class MainWindow
{
    private bool _uninstalling;

    private void UninstallTessera()
    {
        if (_uninstalling) return;
        var owner = _appearanceWindow ?? (Window)this;
        string title = Localization.Text("UninstallTesseraLabel");
        if (WorkspaceBusy || _installingUpdate)
        {
            TesseraDialog.Show(owner, Localization.Text("WaitCurrentOperationFinishBeforeUninstalling"), title);
            return;
        }

        _uninstalling = true;
        try
        {
            var plan = TesseraUninstaller.Prepare(_library.DirectoryPath);
            string message = Localization.Text("TesseraWillClose") +
                "\n\n" + string.Join("\n", plan.Targets);
            if (TesseraDialog.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning,
                    Localization.Text("UninstallTessera"), Localization.Text("Cancel"), confirmByDefault: false) != MessageBoxResult.Yes) return;
            using var process = Process.GetCurrentProcess();
            if (!TryCloseWithHelper(() =>
            {
                using var helper = Process.Start(TesseraUninstaller.CreateStartInfo(plan, process)) ??
                    throw new IOException(Localization.Text("CouldNotStartTheTesseraUninstaller"));
            })) statusLabel.Text = Localization.Text("UninstallingTesseraWasCanceled");
        }
        catch (Exception ex)
        {
            TesseraDialog.Show(owner, Localization.Text("CouldNotStartUninstallingTessera") + "\n\n" + ex.Message,
                title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _uninstalling = false; }
    }
}
