using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tessera.WpfStyles.Themes;

namespace DomainColorTest;

public sealed class AppearanceSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<TesseraTheme>(allowIntegerValues: false) }
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tessera", "appearance.json");

    public TesseraTheme Theme { get; set; } = TesseraTheme.Dark;
    public string Language { get; set; } = "ru";

    public static AppearanceSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppearanceSettings();
            var settings = JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(path), JsonOptions);
            if (settings is null || !Enum.IsDefined(settings.Theme)) return new AppearanceSettings();
            if (settings.Language is not ("ru" or "en")) settings.Language = "ru";
            return settings;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppearanceSettings();
        }
    }

    public void Save(string path)
    {
        if (!Enum.IsDefined(Theme)) throw new ArgumentOutOfRangeException(nameof(Theme));
        if (Language is not ("ru" or "en")) throw new ArgumentOutOfRangeException(nameof(Language));
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
