using System.IO;
using System.Security.Cryptography;

namespace Tessera.Desktop;

internal static class UnrealSyncBridge
{
    public const int PluginVersion = 6;

    public static void InstallPlugin(string projectPath)
    {
        string full = Path.GetFullPath(projectPath);
        if (!File.Exists(full) || !string.Equals(Path.GetExtension(full), ".uproject", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(Localization.Text("ChooseAnExistingUprojectFile"), nameof(projectPath));
        string project = Path.GetDirectoryName(full)!;
        string target = Path.Combine(project, "Plugins", "TesseraSync");
        foreach (string relative in new[] { "Content/Python/tessera_project.py", "Content/Python/init_unreal.py", "TesseraSync.uplugin" })
        {
            string name = "Tessera.Unreal." + Path.GetFileName(relative);
            using var source = typeof(UnrealSyncBridge).Assembly.GetManifestResourceStream(name)
                ?? throw new FileNotFoundException(Localization.Text("ATesserasyncPluginFileIsMissing"), name);
            string destination = Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = File.Create(temporary)) source.CopyTo(output);
                File.Move(temporary, destination, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
