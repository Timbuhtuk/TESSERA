using System.Globalization;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows;
using System.Windows.Markup;

namespace DomainColorTest;

/// <summary>Russian source strings and an embedded English catalog. Language is selected at startup.</summary>
public static class Localization
{
    private static readonly Dictionary<string, string> English;
    private static readonly Dictionary<string, string> Sources;
    private static readonly Dictionary<string, string> Russian;
    private static readonly ConcurrentDictionary<(string Language, string Value), string> Stored = new();
    private static readonly Lazy<(Regex Pattern, string Target)[]> EnglishTemplates = new(() => BuildTemplates(true));
    private static readonly Lazy<(Regex Pattern, string Target)[]> RussianTemplates = new(() => BuildTemplates(false));
    internal static TranslationSource BindingSource { get; } = new();
    public static string Language { get; private set; } = "ru";

    static Localization()
    {
        using var stream = typeof(Localization).Assembly.GetManifestResourceStream("Tessera.Localization.en.json")
            ?? throw new FileNotFoundException("The English translation catalog is missing.");
        English = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        Sources = English.Keys.ToDictionary(Key, source => source);
        Russian = English.GroupBy(pair => pair.Value).ToDictionary(group => group.Key, group => group.First().Key);
    }

    public static void Initialize(string language)
    {
        Language = language == "en" ? "en" : "ru";
        var culture = CultureInfo.GetCultureInfo(Language == "en" ? "en-US" : "ru-RU");
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        BindingSource.Refresh();
    }

    public static string Text(string source) => Language == "en" && English.TryGetValue(source, out var translated) ? translated : source;
    public static string Format(string source, params object?[] values) => string.Format(CultureInfo.CurrentCulture, Text(source), values);
    public static string StoredText(string value)
    {
        // Only call for generated history captions/logs, never for user names or tags.
        var dictionary = Language == "en" ? English : Russian;
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

    private static (Regex Pattern, string Target)[] BuildTemplates(bool english)
    {
        var templates = new List<(Regex, string)>();
        foreach (var pair in English.OrderByDescending(pair => Regex.Replace(english ? pair.Key : pair.Value, @"\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}", "").Length))
        {
            string source = english ? pair.Key : pair.Value;
            string target = english ? pair.Value : pair.Key;
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
    internal static string ByKey(string key) => Text(Sources.TryGetValue(key, out var source) ? source : throw new KeyNotFoundException("Unknown translation: " + key));
    private static string Key(string source) => "Text" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..12];
}

internal sealed class TranslationSource : INotifyPropertyChanged
{
    public string this[string key] => Localization.ByKey(key);
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
        if (target?.TargetProperty is PropertyInfo && target.TargetObject is not Setter) return Localization.ByKey(Key);
        return new Binding("[" + Key + "]") { Source = Localization.BindingSource, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
    }
}
