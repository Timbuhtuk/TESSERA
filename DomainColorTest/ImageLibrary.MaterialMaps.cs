using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace DomainColorTest;

public sealed partial class ImageLibrary
{
    // Only generated map assets have a mutable base. Imported originals and result histories are untouched.
    public SourceEntry StoreMaterialMap(SourceEntry source, BitmapSource image)
    {
        string path = SourcePath(source), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(SourceDirectory(source));
        try
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(temporary)) encoder.Save(stream);
            File.Move(temporary, path, true);
            source.OriginalPath = path; source.Width = image.PixelWidth; source.Height = image.PixelHeight;
            using (var stream = File.OpenRead(path)) source.Fingerprint = Convert.ToHexString(SHA256.HashData(stream));
            source.Thumbnail = ReadThumbnail(path);
            string record = Path.Combine(SourceDirectory(source), "source.json"), recordTemporary = record + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(recordTemporary, JsonSerializer.Serialize(source, JsonOptions));
                File.Move(recordTemporary, record, true);
            }
            finally { if (File.Exists(recordTemporary)) File.Delete(recordTemporary); }
            return source;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
