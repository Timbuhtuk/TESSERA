using System.IO;
using System.Windows;
using Microsoft.Win32;
using Tessera.Core.Imaging;

namespace Tessera.Desktop;

public partial class MainWindow
{
    private UnrealSyncBridge _unrealSync = null!;

    private Task SelectUnrealProjectAsync()
    {
        var dialog = new OpenFileDialog { Title = Localization.Text("Выберите проект Unreal Engine"), Filter = "Unreal Engine project|*.uproject" };
        if (dialog.ShowDialog(this) != true) return Task.CompletedTask;
        _unrealSync.SelectProject(dialog.FileName);
        if (_workspaceCatalog is not null) ConnectUnrealProject(dialog.FileName);
        else statusLabel.Text = Localization.Text("Проект Unreal подключён: ") + _unrealSync.ProjectPath;
        return Task.CompletedTask;
    }

    private Task InstallUnrealPluginAsync()
    {
        if (_workspaceCatalog?.State.ProjectId is { } projectId && UnrealProject(projectId)?.UnrealProjectPath is { } projectPath)
        {
            InstallProjectPlugin(projectPath);
            return Task.CompletedTask;
        }
        EnsureUnrealProject();
        InstallProjectPlugin(_unrealSync.ProjectPath!);
        return Task.CompletedTask;
    }

    private Task SendTextureToUnrealAsync()
    {
        EnsureUnrealProject();
        if (_activeSource is null || !ImageEditorVisible || WorkspaceBusy) throw new InvalidOperationException(Localization.Text("Откройте текстуру в редакторе Tessera."));
        string path = ProcessingBasis == 1 && _activeSource.SelectedGeneration is { } generation
            ? _library.ResultPath(_activeSource, generation) : _library.SourcePath(_activeSource);
        string destination = _unrealSync.SendPng(path, _activeSource.Id, _activeSource.Label);
        statusLabel.Text = Localization.Text("Текстура ожидает импорта в Unreal: ") + destination;
        return Task.CompletedTask;
    }

    private Task SendMapsToUnrealAsync()
    {
        EnsureUnrealProject();
        if (_activeSource is null || WorkspaceBusy) throw new InvalidOperationException(Localization.Text("Откройте исходник с картами материала."));
        var session = _workspaceCatalog?.State.MaterialSessions.FirstOrDefault(s => s.Contains(_activeSource.Id));
        if (session is not null)
        {
            string name = _sources.FirstOrDefault(s => s.Id == session.SourceId)?.Label ?? _activeSource.Label;
            foreach (var kind in Enum.GetValues<MaterialMapKind>())
            {
                var source = _sources.FirstOrDefault(s => s.Id == session.MapId(kind)) ?? throw new InvalidOperationException(Localization.Text("Сначала сохраните карты материала."));
                _unrealSync.SendPng(_library.SourcePath(source), session.SourceId, name, kind.ToString().ToLowerInvariant());
            }
        }
        else
        {
            string prefix = _activeSource.Id.ToString("N") + ":";
            var window = _materialMapWindows.FirstOrDefault(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Value;
            if (window?.Document is null) throw new InvalidOperationException(Localization.Text("Сначала откройте карты материала для выбранного изображения."));
            foreach (var kind in Enum.GetValues<MaterialMapKind>())
            {
                string path = Path.Combine(Path.GetTempPath(), "tessera-map-" + Guid.NewGuid().ToString("N") + ".png");
                try { window.ExportMap(kind, path); _unrealSync.SendPng(path, _activeSource.Id, _activeSource.Label, kind.ToString().ToLowerInvariant()); }
                finally { if (File.Exists(path)) File.Delete(path); }
            }
        }
        statusLabel.Text = Localization.Text("Карты Height, Normal и AO ожидают импорта в Unreal.");
        return Task.CompletedTask;
    }

    private async Task ReceiveFromUnrealAsync()
    {
        EnsureUnrealProject();
        if (WorkspaceBusy) throw new InvalidOperationException(Localization.Text("Дождитесь завершения текущей операции."));
        var pending = _unrealSync.PendingFromUnreal();
        int received = 0;
        foreach (var item in pending)
        {
            await AddImagesAsync([item.Path]);
            if (_sources.Any(source => string.Equals(source.OriginalPath, item.Path, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(source.Fingerprint, item.Transfer.Sha256, StringComparison.OrdinalIgnoreCase)))
            {
                _unrealSync.MarkReceived(item.Transfer);
                received++;
            }
        }
        statusLabel.Text = Localization.Format("Получено текстур из Unreal: {0}.", received);
    }

    private void EnsureUnrealProject()
    {
        if (_unrealSync.ProjectPath is null) throw new InvalidOperationException(Localization.Text("Сначала выберите проект Unreal Engine в меню Файл → Unreal Engine 5."));
    }
}
