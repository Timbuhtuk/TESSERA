using System.Windows;
using System.Windows.Threading;
using Tessera.Core.Imaging;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

public partial class MaterialMapsWindow
{
    private Action<MaterialMapDocument>? _saveWorkspace;
    internal Action<MaterialMapDocument>? SaveToUnreal { get; set; }
    private bool _workspacePending;
    private readonly DispatcherTimer _workspaceSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };

    public void EnableWorkspace(Action<MaterialMapDocument> save)
    {
        _saveWorkspace = save;
        _workspacePending = true;
        openProjectButton.Visibility = saveProjectButton.Visibility = Visibility.Collapsed;
        returnButton.Visibility = Visibility.Visible;
        _workspaceSaveTimer.Tick += (_, _) =>
        {
            if (IsBusy || _strokeBefore is not null) return;
            _workspaceSaveTimer.Stop();
            try { SaveWorkspace(); } catch (Exception ex) { ShowError(ex); }
        };
        Closed += (_, _) => _workspaceSaveTimer.Stop();
    }

    public async Task LoadDocumentAsync(string path, string name)
    {
        SetBusy(true);
        try
        {
            Document = await Task.Run(() => MaterialMapFile.Load(path));
            _workspacePending = false;
            _name = System.IO.Path.GetFileNameWithoutExtension(name); ProjectPath = null; BindDocument();
        }
        finally { SetBusy(false); }
    }

    internal void SetWorkspaceBasis(string basis)
    {
        basisLabel.Text = Localization.Format("BasisFormat", basis);
        basisLabel.Visibility = Visibility.Visible;
    }

    internal async Task ReplaceWorkspaceDocumentAsync(Bitmap image, Action<MaterialMapDocument> save)
    {
        SetBusy(true);
        statusLabel.Text = Localization.Text("GeneratingHeightNormalAndAOAction");
        try
        {
            var replacement = await GenerateDocumentAsync(image);
            save(replacement);
            replacement.MarkSaved();
            _workspacePending = false;
            Document = replacement;
            BindDocument();
        }
        finally { SetBusy(false); }
    }

    public async Task SaveWorkspaceAsync()
    {
        while (IsBusy)
        {
            var pending = _busyCompletion?.Task
                ?? throw new InvalidOperationException(Localization.Text("WaitForTheMapsToFinish"));
            await pending;
        }
        if (!_closed) SaveWorkspace();
    }

    public void SaveWorkspace(bool force = false)
    {
        if (_saveWorkspace is null || Document is null) return;
        if (IsBusy) throw new InvalidOperationException(Localization.Text("WaitForTheMapsToFinish"));
        FlushGeneration();
        if (!force && !Document.HasChanges && !_workspacePending) return;
        _saveWorkspace(Document);
        _workspacePending = false;
        Document.MarkSaved(); UpdateHistory();
        statusLabel.Text = Localization.Text("MapsManualLayersSavedWorkspace");
    }

    private void ScheduleWorkspaceSave()
    {
        if (_saveWorkspace is null || Document?.HasChanges != true) return;
        _workspaceSaveTimer.Stop(); _workspaceSaveTimer.Start();
    }

    private void ReturnClicked(object sender, RoutedEventArgs e) => Close();
}
