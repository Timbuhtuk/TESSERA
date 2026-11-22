using System.IO;
using System.Text.Json;

namespace Tessera.Desktop;

internal static class LibraryJson
{
    public static T Read<T>(string path)
    {
        using var file = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(file) ?? throw new InvalidDataException(path);
    }

    public static void Write<T>(string path, T value) => WriteFile(path, file => JsonSerializer.Serialize(file, value));

    public static void WriteFile(string path, Action<FileStream> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(file);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
