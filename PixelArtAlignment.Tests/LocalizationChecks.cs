using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DomainColorTest;
using Tessera.WpfStyles.Themes;
using Localization = DomainColorTest.Localization;

namespace PixelArtAlignment.Tests;

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
        using var resource = typeof(Localization).Assembly.GetManifestResourceStream("Tessera.Localization.en.json")!;
        var translations = JsonSerializer.Deserialize<Dictionary<string, string>>(resource)!;
        foreach (var pair in translations)
        {
            Require(!string.IsNullOrWhiteSpace(pair.Value), "Empty translation: " + pair.Key);
            Require(CompositeFormat.Parse(pair.Key).MinimumArgumentCount == CompositeFormat.Parse(pair.Value).MinimumArgumentCount,
                "Translation placeholders differ: " + pair.Key);
        }

        string root = Path.GetFullPath(Path.Combine("artifacts", "localization-verification", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root); string settingsPath = Path.Combine(root, "appearance.json");
        File.WriteAllText(settingsPath, "{\"Theme\":\"Light\"}");
        Require(AppearanceSettings.Load(settingsPath).Language == "ru", "Previous settings must default to Russian");
        File.WriteAllText(settingsPath, "{\"Theme\":\"Dark\",\"Language\":\"unknown\"}");
        Require(AppearanceSettings.Load(settingsPath).Language == "ru", "Unknown languages must fall back to Russian");
        new AppearanceSettings { Theme = TesseraTheme.Dark, Language = "en" }.Save(settingsPath);
        var catalog = new WorkspaceCatalog(root); var project = catalog.CreateProject("Русское название");
        var folder = catalog.CreateFolder(project.Id, null, "Фоны");
        var window = new MainWindow(root) { ShowActivated = false };
        try
        {
            window.EnableWorkspacePrototype(); window.Show(); Pump();
            Require(Localization.Language == "en", "Saved language was not loaded on startup");
            Require(Get<Button>(window, "appearanceButton").Content is SettingsIcon { ActualWidth: > 0 }, "Settings SVG did not render");
            Require(AutomationProperties.GetName(Get<Button>(window, "appearanceButton")) == "Settings", "Settings accessibility label is not translated");
            var browser = window.Browser!;
            Require(Get<TextBlock>(browser, "viewTitle").Text == "All assets", "Library title is not in English");
            var node = Get<TreeView>(browser, "projectTree").Items.Cast<TreeViewItem>().Single();
            Require(Equals(node.Header, project.Name) && Equals(((TreeViewItem)node.Items[0]).Header, folder.Name), "Localization changed user project/folder names");
            Require(((MenuItem)node.ContextMenu.Items[0]).Header.Equals("Create folder…"), "Project context menu is not translated");
            Require(new WorkspaceAsset().KindLabel == "Image", "Dynamic material type is not translated");
            Require(Localization.StoredText("РЕЗУЛЬТАТ · 48 × 32 · предыдущие настройки") == "RESULT · 48 × 32 · previous settings", "Existing generated history captions are not translated");

            Get<Button>(window, "appearanceButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            var settings = window.OwnedWindows.OfType<AppearanceWindow>().Single();
            Require(settings.Title == "Settings · TESSERA", "Settings title is not translated");
            Require(Get<ComboBox>(settings, "languageInput").SelectedIndex == 1, "Saved English choice is missing");
            Get<ComboBox>(settings, "languageInput").SelectedIndex = 0; Pump();
            Require(AppearanceSettings.Load(settingsPath).Language == "ru" && Get<TextBlock>(settings, "languageSavedHint").Visibility == Visibility.Visible, "Language choice was not persisted with a restart hint");
            Get<RadioButton>(settings, "lightTheme").IsChecked = true; Pump();
            Require(AppearanceSettings.Load(settingsPath).Language == "ru" && AppearanceSettings.Load(settingsPath).Theme == TesseraTheme.Light, "Changing theme discarded the language choice");
            Require(Localization.Language == "en", "Changing the pending language modified an active editor");
            settings.Close();
            foreach (string name in new[] { "iconWorkspace", "backgroundWorkspace", "asepriteWorkspace", "moodboardWorkspace" })
                Require(Get<FrameworkElement>(window, name) is not null, "Translated workspace failed to initialize: " + name);
            Console.WriteLine($"  {translations.Count} English entries; settings, persistence, user names and old history captions verified");
        }
        finally { window.Close(); Pump(); Localization.Initialize("ru"); }
        var reopened = new MainWindow(root) { ShowActivated = false };
        try
        {
            reopened.EnableWorkspacePrototype(); reopened.Show(); Pump();
            Require(Get<TextBlock>(reopened.Browser!, "viewTitle").Text == "Все ассеты", "Saved Russian language did not apply on reopening");
            Require(AutomationProperties.GetName(Get<Button>(reopened, "appearanceButton")) == "Настройки", "Settings label did not return to Russian");
        }
        finally { reopened.Close(); Pump(); }
    }

    private static T Get<T>(FrameworkElement owner, string name) => (T)owner.FindName(name);
    private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
