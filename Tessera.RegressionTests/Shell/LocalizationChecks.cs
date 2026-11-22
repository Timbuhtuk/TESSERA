using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Tessera.Desktop;
using Tessera.WpfStyles.Themes;
using Localization = Tessera.Desktop.Localization;

namespace Tessera.RegressionTests;

internal static class LocalizationChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(45))) throw new Exception("Localization verification timed out");
        if (failure is not null) { Console.Error.WriteLine(failure); throw new Exception("Localization verification failed", failure); }
    }

    private static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var english = LoadCatalog("en");
        var russian = LoadCatalog("ru");
        Require(english.Count > 0 && english.Keys.ToHashSet().SetEquals(russian.Keys), "Translation keys differ between EN and RU");
        foreach (var (key, en) in english)
        {
            string ru = russian[key];
            Require(Regex.IsMatch(key, @"\A[A-Z][A-Za-z0-9]*\z") && !Regex.IsMatch(key, @"\AText[0-9A-F]{12}\z"),
                "Expected an English identifier: " + key);
            Require(!string.IsNullOrWhiteSpace(en) && !string.IsNullOrWhiteSpace(ru), "Empty translation: " + key);
            Require(CompositeFormat.Parse(en).MinimumArgumentCount == CompositeFormat.Parse(ru).MinimumArgumentCount,
                "Translation placeholders differ: " + key);
        }

        string root = Path.GetFullPath(Path.Combine("artifacts", "localization-verification", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root); string settingsPath = Path.Combine(root, "appearance.json");
        File.WriteAllText(settingsPath, "{\"Theme\":\"Light\"}");
        Require(AppearanceSettings.Load(settingsPath).Language == "ru", "Previous settings must default to Russian");
        Require(AppearanceSettings.Load(settingsPath).ComparisonPreviewKey == Key.Space, "Previous settings must use Space for comparison");
        File.WriteAllText(settingsPath, "{\"Theme\":\"Dark\",\"Language\":\"unknown\"}");
        Require(AppearanceSettings.Load(settingsPath).Language == "ru", "Unknown languages must fall back to Russian");
        new AppearanceSettings { Theme = TesseraTheme.Dark, Language = "en", ComparisonKey = nameof(Key.F7) }.Save(settingsPath);
        var catalog = new WorkspaceCatalog(root); var project = catalog.CreateProject("Русское название");
        var folder = catalog.CreateFolder(project.Id, null, "Фоны");
        var window = new MainWindow(root) { ShowActivated = false };
        try
        {
            window.EnableWorkspacePrototype(); window.Show(); Pump();
            Require(Localization.Language == "en", "Saved language was not loaded on startup");
            Require(Localization.Text("Settings") == "Settings", "English key did not resolve through the English catalog");
            CheckMenu(Get<Menu>(window, "mainMenu").Items);
            CheckMenu(Get<Menu>(window, "libraryMenu").Items);
            Require(Get<Button>(window, "appearanceButton").Content is SettingsIcon { ActualWidth: > 0 }, "Settings SVG did not render");
            Require(AutomationProperties.GetName(Get<Button>(window, "appearanceButton")) == "Settings", "Settings accessibility label is not translated");
            var browser = window.Browser!;
            Require(Get<TextBlock>(browser, "viewTitle").Text == "All assets", "Library title is not in English");
            Require(Get<ListBox>(browser, "assetList").ContextMenu.Items.OfType<MenuItem>().Select(item => item.Header)
                .SequenceEqual(new object[] { "Create board…", "Import files…" }),
                "The background menu is not translated");
            var node = Get<TreeView>(browser, "projectTree").Items.Cast<TreeViewItem>().Single();
            Require(AutomationProperties.GetName(node) == project.Name &&
                AutomationProperties.GetName((TreeViewItem)node.Items[0]) == folder.Name, "Localization changed user project/folder names");
            Require(((MenuItem)node.ContextMenu.Items[0]).Header.Equals("Create folder…"), "Project context menu is not translated");
            Require(new WorkspaceAsset().KindLabel == "Image", "Dynamic material type is not translated");
            var tooltipAsset = new WorkspaceAsset { ImageExtension = ".png", MaterialMaps = (true, false, true) };
            var toolTipFields = tooltipAsset.ToolTipFields.ToDictionary(field => field.Key, field => field.Value);
            Require(toolTipFields["File"] == ".png" && toolTipFields["AO"] == "+" &&
                toolTipFields["Normal"] == "-" && toolTipFields["Height"] == "+", "File or map fields are incorrect in English");
            Require(Localization.StoredText("РЕЗУЛЬТАТ · 48 × 32 · предыдущие настройки") == "RESULT · 48 × 32 · previous settings", "Existing generated history captions are not translated");

            Get<Button>(window, "appearanceButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            var settings = window.OwnedWindows.OfType<AppearanceWindow>().Single();
            Require(settings.Title == "Settings · TESSERA", "Settings title is not translated");
            Require(Get<ComboBox>(settings, "languageInput").SelectedIndex == 1, "Saved English choice is missing");
            Get<ComboBox>(settings, "languageInput").SelectedIndex = 0; Pump();
            Require(AppearanceSettings.Load(settingsPath).Language == "ru" && Get<TextBlock>(settings, "languageSavedHint").Visibility == Visibility.Visible, "Language choice was not persisted with a restart hint");
            Get<RadioButton>(settings, "lightTheme").IsChecked = true; Pump();
            Require(AppearanceSettings.Load(settingsPath).Language == "ru" && AppearanceSettings.Load(settingsPath).Theme == TesseraTheme.Light, "Changing theme discarded the language choice");
            Require(AppearanceSettings.Load(settingsPath).ComparisonPreviewKey == Key.F7, "Changing language or theme discarded the comparison key");
            Require(Localization.Language == "en", "Changing the pending language modified an active editor");
            settings.Close();
            foreach (string name in new[] { "iconWorkspace", "backgroundWorkspace", "asepriteWorkspace", "moodboardWorkspace" })
                Require(Get<FrameworkElement>(window, name) is not null, "Translated workspace failed to initialize: " + name);
        }
        finally { window.Close(); Pump(); Localization.Initialize("ru"); }
        var reopened = new MainWindow(root) { ShowActivated = false };
        try
        {
            reopened.EnableWorkspacePrototype(); reopened.Show(); Pump();
            Require(Localization.Text("Settings") == "Настройки", "English key did not resolve through the Russian catalog");
            Require(Localization.StoredText("RESULT · 48 × 32 · previous settings") == "РЕЗУЛЬТАТ · 48 × 32 · предыдущие настройки",
                "Existing English history captions are not translated");
            Require(Localization.StoredText("Пользовательская подпись") == "Пользовательская подпись", "Unknown history text changed");
            Require(Get<TextBlock>(reopened.Browser!, "viewTitle").Text == "Все ассеты", "Saved Russian language did not apply on reopening");
            Require(AutomationProperties.GetName(Get<Button>(reopened, "appearanceButton")) == "Настройки", "Settings label did not return to Russian");
            Console.WriteLine($"  {english.Count} shared EN/RU keys, language settings, user names and existing history captions verified");
        }
        finally { reopened.Close(); Pump(); }
    }

    private static Dictionary<string, string> LoadCatalog(string language)
    {
        using var resource = typeof(Localization).Assembly.GetManifestResourceStream($"Tessera.Localization.{language}.json")
            ?? throw new Exception("Missing translation catalog: " + language);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(resource)!;
    }

    private static T Get<T>(FrameworkElement owner, string name) => (T)owner.FindControl(name);
    private static void CheckMenu(ItemCollection items)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            string label = item.Header?.ToString() ?? "";
            Require(!label.Any(c => c is >= '\u0400' and <= '\u04ff'), "Untranslated English navigation item: " + label);
            CheckMenu(item.Items);
        }
    }
    private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
