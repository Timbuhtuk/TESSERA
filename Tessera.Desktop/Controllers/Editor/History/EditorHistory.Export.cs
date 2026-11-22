using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using Bitmap = System.Drawing.Bitmap;
using Tessera.Core.Imaging;
using Tessera.Core.Alignment;

namespace Tessera.Desktop;

internal sealed partial class EditorHistory
{
    internal async void SaveAllImages(object sender, RoutedEventArgs e)
    {
        Host.CloseNavigationMenus();
        if (_editor._processing || _editor._activeSource?.Generations.Count is not > 0) return;
        var folderDialog = new OpenFolderDialog { Title = Localization.Text("SaveAllResultsOfTheCurrent") };
        if (folderDialog.ShowDialog(Host) != true) return;

        _editor.SetProcessingState(true);
        Host.statusLabel.Text = Localization.Text("SavingResultsAction");
        try
        {
            var (saved, errors) = await Task.Run(() => SaveAllResults(folderDialog.FolderName));
            string message = errors.Count == 0
                ? Localization.Format("ResultsSavedFormat", saved, folderDialog.FolderName)
                : Localization.Format("SaveCountWithErrors", saved, errors.Count);
            Host.Notify(message, errors.Count == 0 ? NotificationKind.Success : NotificationKind.Warning);
            if (errors.Count == 0) Host.CompleteProcessingProgress();
            if (errors.Count > 0)
                TesseraDialog.Show(Host, string.Join(Environment.NewLine, errors), Localization.Text("SomeResultsWereNotSaved"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Host._mainProgress?.Dispose();
            Host.Notify(Localization.Text("CouldNotSaveResults"), NotificationKind.Error);
            TesseraDialog.Show(Host, ex.Message, Localization.Text("SaveError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _editor.SetProcessingState(false); }
    }

    private (int Saved, List<string> Errors) SaveAllResults(string directory)
    {
        var source = _editor._activeSource ?? throw new InvalidOperationException(Localization.Text("SelectASource"));
        var generations = source.Generations.ToArray();
        if (generations.Length == 0) throw new InvalidOperationException(Localization.Text("ThisSourceHasNoResultsYet"));
        string outputDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(outputDirectory);
        string sourceName = Path.GetFileNameWithoutExtension(source.Label);
        if (string.IsNullOrWhiteSpace(sourceName)) sourceName = "image";
        if (sourceName.Length > 80) sourceName = sourceName[..80];

        int saved = 0;
        var errors = new List<string>();
        for (int q = 0; q < generations.Length; q++)
        {
            var generation = generations[q];
            string original = Host._library.ResultPath(source, generation);
            string stem = $"{sourceName}_result_{q + 1:D3}_{generation.Id.ToString("N")[..8]}";
            string temporary = Path.Combine(outputDirectory, $".{stem}.{Guid.NewGuid():N}.tmp");
            try
            {
                ProgressFile.Copy(original, temporary, new Tessera.Core.Imaging.ProcessingProgress(Host.CurrentProcessingProgress).Slice(q, generations.Length));
                for (int copy = 1; ; copy++)
                {
                    string suffix = copy == 1 ? "" : $"_{copy}";
                    string destination = Path.Combine(outputDirectory, stem + suffix + ".png");
                    try
                    {
                        File.Move(temporary, destination);
                        saved++;
                        break;
                    }
                    catch (IOException) when (File.Exists(destination)) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{generation.Label}: {ex.Message}");
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Host.CurrentProcessingProgress?.Report((q + 1.0) / generations.Length);
        }
        return (saved, errors);
    }
}
