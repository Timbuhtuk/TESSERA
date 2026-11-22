using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using Tessera.WpfStyles.Themes;

namespace Tessera.Desktop;

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
    public int HistoryUndoDepth { get; set; } = 5;
    public string NotificationPosition { get; set; } = nameof(NotificationPlacement.TopRight);
    public string ComparisonKey { get; set; } = nameof(Key.Space);
    public Dictionary<string, string> LensKeys { get; set; } = new();

    internal static IReadOnlyList<(string Tool, string TitleKey, Key DefaultKey)> Lenses { get; } =
    [
        ("contours", "ContoursAction", Key.D1),
        ("gridLens", "PreviewGridAction", Key.D2),
        ("tileHeatmapLens", "EdgeHeatmapAction", Key.D4)
    ];

    internal AppearanceSettings Copy() => (AppearanceSettings)MemberwiseClone();

    internal NotificationPlacement NotificationAnchor =>
        Enum.TryParse<NotificationPlacement>(NotificationPosition, out var position) && Enum.IsDefined(position)
            ? position : NotificationPlacement.TopRight;

    internal Key LensKey(string tool)
    {
        var lens = Lenses.First(lens => lens.Tool == tool);
        return LensKeys.TryGetValue(tool, out string? value) && TryReadShortcutKey(value, out var key)
            ? key : lens.DefaultKey;
    }

    internal Key ComparisonPreviewKey => Enum.TryParse<Key>(ComparisonKey, out var key) && CanBindComparisonKey(key) ? key : Key.Space;

    internal static bool CanBindComparisonKey(Key key) => key is Key.Space or >= Key.D0 and <= Key.Z or
        >= Key.NumPad0 and <= Key.F24 or >= Key.Oem1 and <= Key.Oem3 or >= Key.Oem4 and <= Key.Oem8 or Key.Oem102;

    internal static string ComparisonKeyLabel(Key key) => key == Key.Space ? Localization.Text("Space") : new KeyConverter().ConvertToString(key) ?? key.ToString();

    private static bool TryReadShortcutKey(string? value, out Key key)
        => Enum.TryParse(value, out key) && CanBindComparisonKey(key);

    private void NormalizeLensKeys()
    {
        var keys = new Dictionary<string, string>();
        var usedKeys = new HashSet<Key>();
        foreach (var lens in Lenses)
            if (LensKeys is not null && LensKeys.TryGetValue(lens.Tool, out string? value) &&
                TryReadShortcutKey(value, out var key) && usedKeys.Add(key))
                keys.Add(lens.Tool, key.ToString());
        foreach (var lens in Lenses)
        {
            if (keys.ContainsKey(lens.Tool)) continue;
            var key = usedKeys.Contains(lens.DefaultKey)
                ? Lenses.First(other => !usedKeys.Contains(other.DefaultKey)).DefaultKey : lens.DefaultKey;
            usedKeys.Add(key);
            keys.Add(lens.Tool, key.ToString());
        }
        LensKeys = keys;
    }

    public static AppearanceSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppearanceSettings();
            var settings = JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(path), JsonOptions);
            if (settings is null || !Enum.IsDefined(settings.Theme)) return new AppearanceSettings();
            if (settings.Language is not ("ru" or "en")) settings.Language = "ru";
            settings.HistoryUndoDepth = Math.Clamp(settings.HistoryUndoDepth, 1, 64);
            settings.NotificationPosition = settings.NotificationAnchor.ToString();
            settings.ComparisonKey = settings.ComparisonPreviewKey.ToString();
            settings.NormalizeLensKeys();
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
        if (HistoryUndoDepth is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(HistoryUndoDepth));
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
