using System.Windows;
using System.Windows.Threading;
using PixelArtDownscale;

namespace DomainColorTest;

public partial class MaterialMapsWindow
{
    private Action<MaterialMapDocument>? _saveWorkspace;
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
            _name = name; ProjectPath = null; BindDocument();
        }
        finally { SetBusy(false); }
    }

    public async Task SaveWorkspaceAsync()
    {
        while (IsBusy)
        {
            var pending = _busyCompletion?.Task
                ?? throw new InvalidOperationException(Localization.Text("Дождитесь обновления карт."));
            await pending;
        }
        if (!_closed) SaveWorkspace();
    }

    public void SaveWorkspace(bool force = false)
    {
        if (_saveWorkspace is null || Document is null) return;
        if (IsBusy) throw new InvalidOperationException(Localization.Text("Дождитесь обновления карт."));
        FlushGeneration();
        if (!force && !Document.HasChanges && !_workspacePending) return;
        _saveWorkspace(Document);
        _workspacePending = false;
        Document.MarkSaved(); UpdateHistory();
        statusLabel.Text = Localization.Text("Карты и ручные слои сохранены в рабочем пространстве.");
    }

    private void ScheduleWorkspaceSave()
    {
        if (_saveWorkspace is null || Document?.HasChanges != true) return;
        _workspaceSaveTimer.Stop(); _workspaceSaveTimer.Start();
    }

    private void ReturnClicked(object sender, RoutedEventArgs e) => Close();
}
