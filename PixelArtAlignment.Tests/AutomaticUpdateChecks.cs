using System.Reflection;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DomainColorTest;
using Localization = DomainColorTest.Localization;

namespace PixelArtAlignment.Tests;

internal static class AutomaticUpdateChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                VerifyQuietChecks(); VerifyOffers(); UpdateUiChecks.Run();
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(40))) throw new Exception("Automatic update checks timed out");
        if (failure is not null) throw failure;
    }

    private static MainWindow Window(Func<Task<TesseraRelease?>> check)
    {
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "automatic-update-" + Guid.NewGuid().ToString("N")));
        var window = new MainWindow(folder) { Opacity = 0, ShowInTaskbar = false, ReleaseCheckAsync = check };
        window.EnableWorkspacePrototype(enableUpdates: true);
        // Drive notification ticks explicitly; never let a test open an unattended modal.
        Get<DispatcherTimer>(window, "_updateOfferTimer").Interval = TimeSpan.FromHours(1);
        return window;
    }

    private static TesseraRelease Release(string tag = "v999.0.1")
        => new(new Version(tag[1..]), tag, new Uri("https://github.com/Timbuhtuk/TESSERA/releases/tag/" + tag),
            new Uri("https://github.com/Timbuhtuk/TESSERA/releases/download/" + tag + "/Tessera.exe"), new string('a', 64));

    private static void VerifyQuietChecks()
    {
        int calls = 0;
        var response = new TaskCompletionSource<TesseraRelease?>();
        var window = Window(() => { calls++; return response.Task; });
        try
        {
            window.Show(); Pump();
            Require(calls == 1 && Get<DispatcherTimer>(window, "_updateCheckTimer").IsEnabled &&
                Get<DispatcherTimer>(window, "_updateCheckTimer").Interval == TimeSpan.FromHours(6), "Startup did not schedule automatic checks");
            Wait(Check(window)); Require(calls == 1, "Concurrent checks issued duplicate requests");
            response.SetResult(null); Until(() => !Get<bool>(window, "_checkingUpdates"));
            Require(!window.OwnedWindows.OfType<TesseraDialog>().Any() && !Get<DispatcherTimer>(window, "_updateOfferTimer").IsEnabled,
                "Up-to-date automatic check interrupted the user");
        }
        finally { window.Close(); }
        Require(!Get<DispatcherTimer>(window, "_updateCheckTimer").IsEnabled, "Periodic timer survived closure");

        window = Window(() => throw new HttpRequestException("Offline fixture"));
        try
        {
            window.Show(); Pump();
            Require(!Get<bool>(window, "_checkingUpdates") && !window.OwnedWindows.OfType<TesseraDialog>().Any() &&
                Get<Button>(window, "updateButton").IsEnabled, "Offline startup showed a warning or disabled retry");
        }
        finally { window.Close(); }

        response = new TaskCompletionSource<TesseraRelease?>();
        window = Window(() => response.Task);
        window.Show(); Pump(); window.Close(); response.SetResult(Release());
        Until(() => !Get<bool>(window, "_checkingUpdates"));
        Require(!Get<DispatcherTimer>(window, "_updateOfferTimer").IsEnabled, "Late response scheduled a notification for a closed window");
        Console.WriteLine("  Automatic updates: startup, six-hour schedule, request deduplication, offline/current silence and late response passed.");
    }

    private static void VerifyOffers()
    {
        var window = Window(() => Task.FromResult<TesseraRelease?>(Release()));
        Set(window, "_workspaceImporting", true);
        try
        {
            window.Show(); Pump();
            Require(Get<DispatcherTimer>(window, "_updateOfferTimer").IsEnabled, "New release was not queued");
            Wait(Offer(window)); Require(NoDialog(window), "Notification interrupted processing/import");
            Set(window, "_workspaceImporting", false);
            window.IsEnabled = false; Wait(Offer(window)); Require(NoDialog(window), "Disabled owner received a modal"); window.IsEnabled = true;
            var other = new System.Windows.Window { Owner = window, Width = 200, Height = 120, Opacity = 0, ShowInTaskbar = false };
            try { other.Show(); Pump(); Wait(Offer(window)); Require(NoDialog(window), "Notification covered an existing owned window"); }
            finally { other.Close(); }
            window.WindowState = WindowState.Minimized; Pump();
            Wait(Offer(window)); Require(NoDialog(window), "Minimized window received a modal");
            window.WindowState = WindowState.Normal; window.Activate(); Pump();
            Require(window.IsActive, "Test owner could not activate");
            int offers = Dismiss(window, () => Offer(window), "Позже");
            Require(offers == 1 && Get<HashSet<string>>(window, "_notifiedReleaseTags").Contains(Release().Tag) && window.IsEnabled,
                "Declining did not leave the application usable");
            Wait(Offer(window)); Wait(Check(window));
            Require(NoDialog(window) && !Get<DispatcherTimer>(window, "_updateOfferTimer").IsEnabled, "Same release nagged after decline");
            Require(Dismiss(window, () => Check(window, true), "Позже") == 1, "Manual retry did not offer the known release");
            window.ReleaseCheckAsync = () => throw new HttpRequestException("Later offline fixture");
            Wait(Check(window)); Require(NoDialog(window), "Background retry showed an error");
            Localization.Initialize("en");
            window.ReleaseCheckAsync = () => Task.FromResult<TesseraRelease?>(Release("v999.0.2"));
            Wait(Check(window)); window.Activate(); Pump();
            Require(Dismiss(window, () => Offer(window), "Later") == 1, "A newer release was suppressed after declining the earlier one");
            window.ReleaseCheckAsync = () => Task.FromResult<TesseraRelease?>(Release());
            Wait(Check(window));
            Require(!Get<DispatcherTimer>(window, "_updateOfferTimer").IsEnabled, "Returning to a previously offered release repeated its notification");
            Require(!Get<bool>(window, "_installingUpdate") && !Get<DispatcherTimer>(window, "_updateOfferTimer").IsEnabled,
                "Dismissal left installation or notification active");
        }
        finally { Localization.Initialize("ru"); Set(window, "_workspaceImporting", false); window.Close(); }
        Console.WriteLine("  Automatic update modal: busy/owned/minimized guards, decline, one offer per version, manual retry and RU/EN passed without download/install.");
    }

    private static int Dismiss(MainWindow owner, Func<Task> action, string later)
    {
        int offers = 0; Exception? failure = null; bool invoking = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            foreach (var dialog in owner.OwnedWindows.OfType<TesseraDialog>().ToArray())
            {
                offers++;
                try
                {
                    Require(invoking && dialog.Owner == owner && dialog.IsVisible, "Update offer did not block the owner action until dismissed");
                    Require(((TextBlock)dialog.FindName("dialogMessage")).Text.Contains(Get<TesseraRelease>(owner, "_availableRelease").Tag), "Offer lost the release version");
                    var buttons = ((StackPanel)dialog.FindName("dialogButtons")).Children.OfType<Button>().ToArray();
                    Require(buttons.Single(b => Equals(b.Tag, MessageBoxResult.No)).Content.Equals(later), "Dismiss label is not localized");
                    Require(buttons.Single(b => Equals(b.Tag, MessageBoxResult.Yes)).IsDefault, "Accept action lost keyboard default");
                }
                catch (Exception ex) { failure = ex; }
                ((StackPanel)dialog.FindName("dialogButtons")).Children.OfType<Button>().Single(b => Equals(b.Tag, MessageBoxResult.No))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        };
        timer.Start();
        try { var task = action(); invoking = false; Wait(task); }
        finally { timer.Stop(); }
        if (failure is not null) throw failure;
        return offers;
    }

    private static bool NoDialog(MainWindow window) => !window.OwnedWindows.OfType<TesseraDialog>().Any();
    private static Task Check(MainWindow window, bool manual = false) => (Task)typeof(MainWindow).GetMethod("CheckForUpdatesAsync", Private)!.Invoke(window, [manual])!;
    private static Task Offer(MainWindow window) => (Task)typeof(MainWindow).GetMethod("OfferUpdateWhenReadyAsync", Private)!.Invoke(window, null)!;
    private static T Get<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, Private)!.GetValue(window)!;
    private static void Set(MainWindow window, string name, object value) => typeof(MainWindow).GetField(name, Private)!.SetValue(window, value);
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Until(Func<bool> condition)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!condition()) { if (timer.Elapsed > TimeSpan.FromSeconds(8)) throw new Exception("Automatic update action timed out"); Pump(); Thread.Sleep(5); }
        Pump();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
