using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Bitmap = System.Drawing.Bitmap;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

namespace Tessera.Desktop;

public partial class MainWindow
{
    private static bool ClipboardHasImage()
    {
        try { return Clipboard.ContainsData("PNG") || Clipboard.ContainsImage(); }
        catch { return false; }
    }

    private void CopySelectedResultToClipboard()
    {
        if (_processing || _activeSource is not { } source ||
            generationStrip.SelectedItem is not GenerationEntry generation || !source.Generations.Contains(generation)) return;
        try
        {
            using var image = ImageLibrary.ReadBitmap(_library.ResultPath(source, generation));
            Clipboard.SetDataObject(CreateResultClipboardData(image), true);
            statusLabel.Text = Localization.Text("Результат скопирован в буфер обмена как PNG.");
        }
        catch (Exception ex) { statusLabel.Text = Localization.Format("Не удалось скопировать результат: {0}", ex.Message); }
    }

    private void PasteClipboardAsBranch()
    {
        try { PasteClipboardImageAsBranch(Clipboard.GetDataObject()); }
        catch (Exception ex) { statusLabel.Text = Localization.Format("Не удалось вставить изображение: {0}", ex.Message); }
    }

    private void PasteClipboardImageAsBranch(IDataObject? data)
    {
        if (_processing || _activeSource is not { } source) return;
        try
        {
            using var image = ReadClipboardImage(data);
            if (image is null)
            {
                statusLabel.Text = Localization.Text("В буфере обмена нет изображения.");
                return;
            }
            var entry = _library.SaveGeneration(source, image, null, new GenerationEntry
            {
                Operation = "Буфер обмена",
                OperationId = "clipboard.paste",
                Caption = "Вставлено из буфера обмена",
                Log = Localization.Format("PNG из буфера обмена · {0} × {1} px", image.Width, image.Height),
                Settings = CaptureSettings(),
                PreservesTransparency = true,
                RecipeUnavailableReason = "Изображение вставлено из буфера обмена; исходная операция неизвестна."
            });
            if (CommitGeneration(source, entry))
                statusLabel.Text = Localization.Text("Изображение вставлено отдельной веткой истории.");
        }
        catch (Exception ex) { statusLabel.Text = Localization.Format("Не удалось вставить изображение: {0}", ex.Message); }
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
