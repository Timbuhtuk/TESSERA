using System.Globalization;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows;
using System.Windows.Markup;

namespace Tessera.Desktop;

/// <summary>Embedded English and Russian catalogs share stable message keys.</summary>
public static class Localization
{
    private static readonly Dictionary<string, string> English = LoadCatalog("en");
    private static readonly Dictionary<string, string> Russian = LoadCatalog("ru");
    private static readonly Dictionary<string, string> LegacyToEnglish = new();
    private static readonly Dictionary<string, string> LegacyToRussian = new();
    private static readonly ConcurrentDictionary<(string Language, string Value), string> Stored = new();
    private static readonly Lazy<(Regex Pattern, string Target)[]> EnglishTemplates = new(() => BuildTemplates(LegacyToEnglish));
    private static readonly Lazy<(Regex Pattern, string Target)[]> RussianTemplates = new(() => BuildTemplates(LegacyToRussian));
    internal static TranslationSource BindingSource { get; } = new();
    public static string Language { get; private set; } = "ru";

    static Localization()
    {
        foreach (var (key, english) in English)
        {
            string russian = Russian[key];
            LegacyToEnglish.TryAdd(russian, english);
            LegacyToRussian.TryAdd(english, russian);
        }
    }

    private static Dictionary<string, string> LoadCatalog(string language)
    {
        string resource = $"Tessera.Localization.{language}.json";
        using var stream = typeof(Localization).Assembly.GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException("Translation catalog is missing.", resource);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException("Empty translation catalog: " + resource);
    }

    public static void Initialize(string language)
    {
        Language = language == "en" ? "en" : "ru";
        var culture = CultureInfo.GetCultureInfo(Language == "en" ? "en-US" : "ru-RU");
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        BindingSource.Refresh();
    }

    public static string Text(string key) => (Language == "en" ? English : Russian)[key];
    public static string Format(string key, params object?[] values) => string.Format(CultureInfo.CurrentCulture, Text(key), values);

    // Existing history stores generated labels and logs as RU or EN text.
    public static string StoredText(string value)
    {
        var dictionary = Language == "en" ? LegacyToEnglish : LegacyToRussian;
        if (dictionary.TryGetValue(value, out var translated)) return translated;
        return Stored.GetOrAdd((Language, value), key =>
        {
            foreach (var template in key.Language == "en" ? EnglishTemplates.Value : RussianTemplates.Value)
            {
                var match = template.Pattern.Match(key.Value);
                if (match.Success) return Regex.Replace(template.Target, @"\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}", m => match.Groups["arg" + m.Groups[1].Value].Value);
            }
            return key.Value;
        });
    }

    private static (Regex Pattern, string Target)[] BuildTemplates(Dictionary<string, string> translations)
    {
        var templates = new List<(Regex, string)>();
        foreach (var (source, target) in translations.OrderByDescending(pair => Regex.Replace(pair.Key, @"\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}", "").Length))
        {
            if (!source.Contains('{')) continue;
            var slots = Regex.Matches(source, @"\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}");
            if (slots.Count == 0) continue;
            var pattern = new StringBuilder("\\A"); int start = 0;
            foreach (Match slot in slots)
            {
                pattern.Append(Regex.Escape(source[start..slot.Index])).Append("(?<arg").Append(slot.Groups[1].Value).Append(">.*?)");
                start = slot.Index + slot.Length;
            }
            pattern.Append(Regex.Escape(source[start..])).Append("\\z");
            templates.Add((new Regex(pattern.ToString(), RegexOptions.Singleline, TimeSpan.FromMilliseconds(100)), target));
        }
        return templates.ToArray();
    }
}

internal sealed class TranslationSource : INotifyPropertyChanged
{
    public string this[string key] => Localization.Text(key);
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TranslateExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var target = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        if (target?.TargetProperty is PropertyInfo && target.TargetObject is not Setter) return Localization.Text(Key);
        return new Binding("[" + Key + "]") { Source = Localization.BindingSource, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
    }
}
