using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DomainColorTest;

namespace PixelArtAlignment.Tests;

internal static class UpdateUiChecks
{
    public static void Run()
    {
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "update-ui-" + Guid.NewGuid().ToString("N")));
        var window = new MainWindow(folder) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        bool closed = false;
        int launches = 0;
        window.Closed += (_, _) => closed = true;
        try
        {
            window.Show(); Pump();
            SetBusy(true);
            Require(!TryClose(() => launches++) && !closed && launches == 0, "Update helper started while processing blocked closure");
            SetBusy(false);
            CancelEventHandler cancel = (_, e) => e.Cancel = true;
            window.Closing += cancel;
            var inspector = new TileInspectorWindow { Owner = window, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            int unexpectedDialogs = 0;
            var dismissUnexpectedDialog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            dismissUnexpectedDialog.Tick += (_, _) =>
            {
                foreach (var dialog in inspector.OwnedWindows.OfType<TesseraDialog>().ToArray())
                {
                    unexpectedDialogs++;
                    ((StackPanel)dialog.FindName("dialogButtons")).Children.OfType<Button>().Single(button => Equals(button.Tag, MessageBoxResult.No)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            };
            try
            {
                inspector.Show(); Pump();
                typeof(TileInspectorWindow).GetField("_projectDirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(inspector, true);
                dismissUnexpectedDialog.Start();
                Require(!TryClose(() => launches++) && !closed && launches == 0 && inspector.IsVisible && unexpectedDialogs == 0,
                    "Canceled closure prompted again or started the update helper");
            }
            finally { dismissUnexpectedDialog.Stop(); inspector.DiscardAndClose(); window.Closing -= cancel; }
            try { TryClose(() => throw new IOException("Cannot start helper")); throw new Exception("Start error was hidden"); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException { InnerException: IOException }) { }
            Require(window.IsVisible && !closed, "Failed updater launch closed the application");
            // Repeat after rejection to verify that temporary Closing handlers were removed.
            SetBusy(true);
            Require(!TryClose(() => launches++) && launches == 0, "Rejected update left an installer handler attached");
            SetBusy(false);
            Require(TryClose(() => { Require(!closed, "Helper must start before process exit"); launches++; }) && closed && launches == 1,
                "Accepted closure did not start exactly one helper");
            Console.WriteLine("  Update UI: processing/user close guards, launch failure, canceled attempts, retry and exactly one approved helper passed without installing an update.");
        }
        finally { if (!closed) { SetBusy(false); window.Close(); } }

        bool TryClose(Action start) => (bool)typeof(MainWindow).GetMethod("TryCloseForUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { start })!;
        void SetBusy(bool busy) => typeof(MainWindow).GetMethod("SetProcessingState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { busy });
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
