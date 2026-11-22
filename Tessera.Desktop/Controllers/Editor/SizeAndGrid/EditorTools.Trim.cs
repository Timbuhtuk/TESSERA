using System.Windows;
using Tessera.Core.Imaging;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

internal sealed partial class EditorTools
{
    internal async void TrimTransparentEdges(object? sender, RoutedEventArgs e)
    {
        var basis = _editor.ProcessingBasis == 1 && _editor._downscaledResult is not null
            ? _editor._downscaledResult : _editor._sourceImage;
        if (_editor._processing || basis is null || _editor._activeSource is null) return;

        var document = _editor._activeSource;
        Guid? parentId = ReferenceEquals(basis, _editor._downscaledResult) ? document.SelectedGeneration?.Id : null;
        var settings = CaptureSettings();
        using var input = (Bitmap)basis.Clone();
        _editor.SetProcessingState(true);
        Host.statusLabel.Text = Localization.Text("TrimmingTransparentEdgesAction");
        try
        {
            var generation = await Task.Run(() =>
            {
                using var output = BitmapFactory.CropTransparentEdges(input, Host.CurrentProcessingProgress);
                return Host._library.SaveGeneration(document, output, null, new GenerationEntry
                {
                    Settings = settings,
                    OperationId = ProcessingOperations.Trim,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = ProcessingOperations.Trim }),
                    Operation = Localization.Text("TrimTransparentEdgesLabel"),
                    ParentGenerationId = parentId,
                    PreservesTransparency = System.Drawing.Image.IsAlphaPixelFormat(output.PixelFormat),
                    Caption = Localization.Format("TRIMFormat", output.Width, output.Height),
                    Log = Localization.Format("TransparentEdgesTrimmedFormat", input.Width, input.Height, output.Width, output.Height)
                });
            });
            if (!_editor.History.CommitGeneration(document, generation)) return;
            Host.Notify(Localization.Format("TrimSavedToHistoryFormat", generation.Width, generation.Height), NotificationKind.Success);
        }
        catch (Exception ex)
        {
            Host._mainProgress?.Dispose();
            Host.Notify(Localization.Text("CouldNotTrimTransparentEdges"), NotificationKind.Error);
            TesseraDialogs.Show(Host, ex.Message, Localization.Text("TrimError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _editor.SetProcessingState(false); }
    }
}
