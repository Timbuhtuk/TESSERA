using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Bitmap = System.Drawing.Bitmap;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

namespace Tessera.Desktop;

internal sealed partial class EditorHistory
{
    private static bool ClipboardHasImage()
    {
        try { return Clipboard.ContainsData("PNG") || Clipboard.ContainsImage(); }
        catch { return false; }
    }

    private void CopySelectedResultToClipboard()
    {
        if (_editor._processing || _editor._activeSource is not { } source ||
            _editor.generationStrip.SelectedItem is not GenerationEntry generation || !source.Generations.Contains(generation)) return;
        try
        {
            using var image = ImageLibrary.ReadBitmap(Host._library.ResultPath(source, generation));
            Clipboard.SetDataObject(CreateResultClipboardData(image), true);
            Host.Notify(Localization.Text("ResultCopiedAsPng"), NotificationKind.Success);
        }
        catch (Exception ex) { Host.Notify(Localization.Format("CopyResultFailed", ex.Message), NotificationKind.Error); }
    }

    private void PasteClipboardAsBranch()
    {
        try { PasteClipboardImageAsBranch(Clipboard.GetDataObject()); }
        catch (Exception ex) { Host.Notify(Localization.Format("PasteImageFailed", ex.Message), NotificationKind.Error); }
    }

    private void PasteClipboardImageAsBranch(IDataObject? data)
    {
        if (_editor._processing || _editor._activeSource is not { } source) return;
        try
        {
            using var image = ReadClipboardImage(data);
            if (image is null)
            {
                Host.Notify(Localization.Text("ClipboardHasNoImage"), NotificationKind.Warning);
                return;
            }
            var entry = Host._library.SaveGeneration(source, image, null, new GenerationEntry
            {
                Operation = Localization.Text("Clipboard"),
                OperationId = "clipboard.paste",
                Caption = Localization.Text("PastedFromClipboard"),
                Log = Localization.Format("ClipboardPngCaption", image.Width, image.Height),
                Settings = _editor.Tools.CaptureSettings(),
                PreservesTransparency = true,
                RecipeUnavailableReason = Localization.Text("ClipboardOriginalOperationUnknown")
            });
            if (CommitGeneration(source, entry))
                Host.Notify(Localization.Text("ImagePastedAsHistoryBranch"), NotificationKind.Success);
        }
        catch (Exception ex) { Host.Notify(Localization.Format("PasteImageFailed", ex.Message), NotificationKind.Error); }
    }

    private static DataObject CreateResultClipboardData(Bitmap image)
    {
        using var png = new MemoryStream();
        image.Save(png, ImageFormat.Png);
        png.Position = 0;
        var frame = BitmapFrame.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        frame.Freeze();
        var data = new DataObject();
        data.SetData("PNG", new MemoryStream(png.ToArray()), false);
        data.SetImage(frame);
        return data;
    }

    private static Bitmap? ReadClipboardImage(IDataObject? data)
    {
        if (data is null) return null;
        if (data.GetDataPresent("PNG", false))
        {
            object? raw = data.GetData("PNG", false);
            using var png = new MemoryStream();
            if (raw is Stream stream)
            {
                if (stream.CanSeek) stream.Position = 0;
                stream.CopyTo(png);
            }
            else if (raw is byte[] bytes) png.Write(bytes);
            if (png.Length > 0)
            {
                png.Position = 0;
                using var decoded = new Bitmap(png);
                return (Bitmap)decoded.Clone();
            }
        }
        if (data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var png = new MemoryStream();
            encoder.Save(png); png.Position = 0;
            using var decoded = new Bitmap(png);
            return (Bitmap)decoded.Clone();
        }
        if (data.GetData(DataFormats.Bitmap) is Bitmap drawing) return (Bitmap)drawing.Clone();
        return null;
    }
}
