using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using Tessera.WpfStyles.Themes;
using Localization = DomainColorTest.Localization;

namespace PixelArtAlignment.Tests;

internal static class ReleaseNotesChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(45))) throw new Exception("Release notes checks timed out");
        if (failure is not null) { Console.Error.WriteLine(failure); throw new Exception("Release notes checks failed", failure); }
    }

    private static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string root = Path.GetFullPath(Path.Combine("artifacts", "wpf", "patch-notes", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string settingsPath = Path.Combine(root, "appearance.json");
        string statePath = Path.Combine(root, "release-notes-state.json");
        var catalog = ReleaseNotesCatalog.Bundled;
        Require(catalog.Current.Version == GitHubUpdater.CurrentVersion.ToString(3) && catalog.Releases.Count >= 2, "Bundled notes lost the current version or history");
        var state = new ReleaseNotesState(statePath);
        Require(!state.HasSeen(catalog.Current.Version), "New library incorrectly marks notes as seen");
        var first = OpenMain();
        try
        {
            var popup = first.OwnedWindows.OfType<ReleaseNotesWindow>().Single();
            Require(state.HasSeen(catalog.Current.Version), "Actually rendered startup notes were not remembered");
            Require(popup.Title == "Что нового в Tessera", "Russian startup title is missing");
            Capture(popup, Path.Combine(root, "startup-ru.png"));
            Get<Button>(popup, "continueButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(!first.OwnedWindows.OfType<ReleaseNotesWindow>().Any(), "Continue did not close the startup notes");
        }
        finally { first.Close(); Pump(); }

        var restarted = OpenMain();
        try
        {
            Require(!restarted.OwnedWindows.OfType<ReleaseNotesWindow>().Any(), "Same version showed startup notes twice");
            NavigationTest.Click(restarted, "appearanceButton"); Pump();
            var settings = restarted.OwnedWindows.OfType<AppearanceWindow>().Single();
            settings.Opacity = 0;
            var tabs = Get<TabControl>(settings, "settingsTabs");
            var patchTab = Get<TabItem>(settings, "patchNotesTab");
            Require(Equals(patchTab.Header, "Патчноуты"), "Settings has no dedicated patch notes tab");
            tabs.SelectedItem = patchTab; Pump();
            var view = Get<ReleaseNotesView>(settings, "releaseNotesView");
            var picker = Get<ComboBox>(view, "releasePicker");
            Require(picker.Items.Count == catalog.Releases.Count, "Settings lost archived versions");
            picker.SelectedIndex = 1; Pump();
            Require(Get<TextBlock>(view, "releaseHeading").Text == catalog.Releases[1].Heading, "Selecting a previous version did not change the notes");
            Require(new TextRange(Get<FlowDocumentScrollViewer>(view, "notesReader").Document.ContentStart,
                Get<FlowDocumentScrollViewer>(view, "notesReader").Document.ContentEnd).Text.Length > 30, "Historical notes are empty");
            picker.SelectedIndex = 0;
            foreach (var theme in new[] { TesseraTheme.Dark, TesseraTheme.Light, TesseraTheme.Contrast })
            {
                AppearanceResources.Apply(theme); Pump();
                settings.Width = 680; settings.Height = 650; Pump();
                Capture(settings, Path.Combine(root, theme + "-settings.png"));
                settings.Width = 440; settings.Height = 400; Pump();
                Require(view.ActualWidth > 300 && view.ActualHeight > 150 && Get<FlowDocumentScrollViewer>(view, "notesReader").ActualHeight > 60,
                    "Patch notes cannot be read at minimum size");
                Capture(settings, Path.Combine(root, theme + "-settings-minimum.png"));
            }
            tabs.SelectedItem = Get<TabItem>(settings, "generalTab"); Pump();
            Require(Get<ComboBox>(settings, "languageInput").IsVisible && Get<StackPanel>(settings, "updatesHost").Children.Count == 1,
                "General settings lost language or updates");
        }
        finally { restarted.Close(); Pump(); }

        // State persists all seen versions: upgrades show once, downgrades do not reset history.
        string futureVersion = "99.0.0";
        Require(!state.HasSeen(futureVersion), "Unseen upgrade was suppressed");
        var future = new ReleaseNotesCatalog { Releases = [new ReleaseNote { Version = futureVersion, Title = new() { ["en"] = "Future" }, Notes = new() { ["en"] = "Future changes" } }, catalog.Current] };
        Localization.Initialize("en");
        var upgraded = new ReleaseNotesWindow(future, state) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            Require(!state.HasSeen(futureVersion), "Constructing a window marked unread notes as seen");
            upgraded.Show(); Pump();
            Require(state.HasSeen(futureVersion) && state.HasSeen(catalog.Current.Version), "Upgrade lost previously seen versions");
            Require(upgraded.Title == "What's new in Tessera" && Equals(Get<Button>(upgraded, "continueButton").Content, "Continue"), "English notes chrome is untranslated");
            Capture(upgraded, Path.Combine(root, "startup-en.png"));
        }
        finally { upgraded.Close(); Pump(); }
        File.WriteAllText(statePath, "not json");
        Require(!state.HasSeen(futureVersion) && state.MarkSeen(futureVersion), "Corrupt state prevented displaying or remembering notes");
        string blocked = Path.Combine(root, "not-a-directory"); File.WriteAllText(blocked, "keep");
        var unwritable = new ReleaseNotesState(Path.Combine(blocked, "state.json"));
        var warning = new ReleaseNotesWindow(future, unwritable) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            warning.Show(); Pump();
            Require(Get<TextBlock>(warning, "saveWarning").Visibility == Visibility.Visible && !unwritable.HasSeen(futureVersion),
                "State write failure was hidden or crashed the reader");
        }
        finally { warning.Close(); Pump(); }
        foreach (string invalid in new[] { "{}", "{\"releases\":[]}", "{\"releases\":[{\"version\":\"1.0.0\"}]}" })
        {
            try { ReleaseNotesCatalog.Parse(invalid, new Version(1, 0, 0)); throw new Exception("Invalid release catalog was accepted"); }
            catch (InvalidDataException) { }
        }
        Localization.Initialize("ru"); AppearanceResources.Apply(TesseraTheme.Dark);
        Console.WriteLine("  Release notes: offline bundle, rendered first launch, restart suppression, upgrade/downgrade state, settings history, RU/EN, three themes, minimum layout and failed writes passed.");

        MainWindow OpenMain()
        {
            var window = new MainWindow(root) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            window.EnableReleaseNotes(); window.EnableReleaseNotes(); window.Show(); Pump();
            return window;
        }
    }

    private static T Get<T>(FrameworkElement owner, string name) => (T)owner.FindName(name);
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Capture(FrameworkElement element, string path)
    {
        Pump(); element.UpdateLayout();
        if (element is Window window) element = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
