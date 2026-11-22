using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;
using Tessera.Desktop;
using Point = System.Windows.Point;

namespace Tessera.RegressionTests;

internal static class WindowChromeChecks
{
    public static void Run()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var directory = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "chrome-" + Guid.NewGuid().ToString("N")));
        // Native minimize/restore must use the same visible, taskbar-backed window as the desktop app.
        var window = new MainWindow(directory) { ShowActivated = true };
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        try
        {
            window.Show(); Pump();
            var handle = new WindowInteropHelper(window).Handle;
            Require(WindowChrome.GetWindowChrome(window) is { UseAeroCaptionButtons: false }, "Native title bar was not replaced");
            var caption = (Border)window.FindControl("titleBar");
            var menu = (Menu)window.FindControl("mainMenu");
            var buttons = (StackPanel)window.FindControl("captionButtons");
            Require(menu.Visibility == Visibility.Collapsed, "The library exposes the editor menu");
            WindowTestAccess.Invoke(window, "ShowEditor", BindingFlags.Instance | BindingFlags.NonPublic, null); Pump();
            VerifyDragStrip();
            double menuRight = menu.TranslatePoint(new Point(menu.ActualWidth, 0), caption).X;
            double buttonsLeft = buttons.TranslatePoint(new Point(), caption).X;
            foreach (double fraction in new[] { 0.1, 0.5, 0.9 })
                Require(HitTest(handle, caption.PointToScreen(new Point(menuRight + (buttonsLeft - menuRight) * fraction, 20))) == 2,
                    "Empty title bar area cannot be dragged");
            var fileMenu = (MenuItem)window.FindControl("fileMenu");
            Require(HitTest(handle, fileMenu.PointToScreen(new Point(10, 20))) == 1, "Top menu is swallowed by native dragging");
            var appearanceButton = (Button)window.FindControl("appearanceButton");
            Require(HitTest(handle, appearanceButton.PointToScreen(new Point(20, 20))) == 1, "Settings button is swallowed by native dragging");
            var close = (Button)window.FindControl("closeButton");
            Require(HitTest(handle, close.PointToScreen(new Point(20, 20))) == 1, "Caption button is swallowed by native dragging");
            Require(GetWindowRect(handle, out var normal), "Cannot read window bounds");
            double normalWidth = window.ActualWidth;
            double normalHeight = window.ActualHeight;
            Require(caption.PointToScreen(new Point(0, 0)).Y - normal.Top < 8, "An extra native title bar remains above the custom caption");
            Require(HitTest(handle, caption.PointToScreen(new Point(caption.ActualWidth / 2, 1))) == 12, "Top resize border is not active");
            Require(HitTest(handle, new Point(normal.Left + 2, normal.Top + 120)) == 10, "Left resize border is not active");
            Require(HitTest(handle, new Point(normal.Right - 2, normal.Bottom - 2)) == 17, "Corner resize border is not active");
            Require(HitTest(handle, caption.PointToScreen(new Point(caption.ActualWidth / 2, caption.ActualHeight + 2))) == 1,
                "Drag area extends into the workspace");

            Click("maximizeButton");
            WaitFor(() => window.WindowState == WindowState.Maximized, "Maximize button failed");
            VerifyDragStrip();
            Require(AutomationProperties.GetName((Button)window.FindControl("maximizeButton")) == "Восстановить", "Restore label did not update");
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            Require(GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor), "Cannot read monitor work area");
            var content = (Grid)window.FindControl("rootLayout");
            var topLeft = content.PointToScreen(new Point(0, 0));
            var bottomRight = content.PointToScreen(new Point(content.ActualWidth, content.ActualHeight));
            Require(topLeft.X >= monitor.Work.Left - 1 && topLeft.Y >= monitor.Work.Top - 1 &&
                bottomRight.X <= monitor.Work.Right + 1 && bottomRight.Y <= monitor.Work.Bottom + 1,
                $"Maximized content extends outside the work area: {topLeft}–{bottomRight}, work {monitor.Work.Left},{monitor.Work.Top}–{monitor.Work.Right},{monitor.Work.Bottom}");
            Click("maximizeButton");
            // Windows can constrain the initial window to the runner's display size.
            WaitFor(() => window.WindowState == WindowState.Normal && Math.Abs(window.ActualWidth - normalWidth) < 1 && Math.Abs(window.ActualHeight - normalHeight) < 1,
                $"Restore lost the window dimensions: expected {normalWidth}x{normalHeight}, got {window.ActualWidth}x{window.ActualHeight}");

            var point = caption.PointToScreen(new Point(caption.ActualWidth / 2, 5));
            SendMessage(handle, 0xA3, 2, Pack(point));
            Pump();
            WaitFor(() => window.WindowState == WindowState.Maximized, "Native title double-click failed");
            Click("maximizeButton");
            Click("minimizeButton");
            WaitFor(() => window.WindowState == WindowState.Minimized, "Minimize button failed");
            SystemCommands.RestoreWindow(window); Pump();
            WaitFor(() => window.WindowState == WindowState.Normal, "Restore from minimized failed");

            var dialog = TesseraDialogs.Create("Проверка", "Tessera");
            dialog.Owner = window;
            dialog.Show(); Pump();
            ((Button)dialog.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(window.WindowState == WindowState.Normal && !IsIconic(handle) && window.IsVisible,
                "Closing an owned dialog minimized the main window");
            WindowTestAccess.Invoke(window, "OpenAppearance", BindingFlags.Instance | BindingFlags.NonPublic, [window, new RoutedEventArgs()]);
            Pump();
            var appearance = window.OwnedWindows.Cast<Window>().Single(w => w is AppearanceWindow);
            appearance.Close(); Pump();
            Require(window.WindowState == WindowState.Normal && !IsIconic(handle) && window.IsVisible,
                "Closing an owned settings window minimized the main window");
            foreach (var child in new Window[] { new ColorReplaceWindow(System.Drawing.Color.Red) })
            {
                child.Owner = window;
                Require(!child.ShowInTaskbar, "A child window created another taskbar entry");
                child.Show(); Pump();
                child.Close(); Pump();
                Require(!child.IsVisible, "An owned workspace window refused to close: " + child.GetType().Name);
                Require(window.WindowState == WindowState.Normal && !IsIconic(handle) && window.IsVisible,
                    "Closing an owned workspace window minimized the main window");
            }

            SetBusy(true);
            Click("closeButton");
            Require(!closed && window.IsVisible, "Custom close bypassed the processing guard");
            SetBusy(false);
            Click("closeButton");
            WaitFor(() => closed, "Custom close did not close the window");
            Console.WriteLine("  Window chrome: caption, resize, minimize/restore, owned windows and guarded close passed.");

            void VerifyDragStrip()
            {
                foreach (double fraction in new[] { 0.05, 0.15, 0.3, 0.55, 0.85, 0.97 })
                    Require(HitTest(handle, caption.PointToScreen(new Point(caption.ActualWidth * fraction, 5))) == 2,
                        $"Upper title bar does not allow dragging at {fraction:P0} in {window.WindowState} state");
            }
        }
        finally
        {
            if (!closed) { SetBusy(false); window.Close(); }
        }

        void Click(string name) { ((Button)window.FindControl(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
        void SetBusy(bool value) => WindowTestAccess.Invoke(window, "SetProcessingState", BindingFlags.Instance | BindingFlags.NonPublic, [value]);
        void WaitFor(Func<bool> condition, string message)
        {
            // SystemCommands posts a native window message; WPF can update ActualWidth/Height on a later layout pass.
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            bool delayed = !condition();
            if (delayed) Console.WriteLine($"  Window chrome pending: {message}; state {window.WindowState}, size {window.ActualWidth}x{window.ActualHeight}.");
            while (!condition() && elapsed.Elapsed < TimeSpan.FromSeconds(2)) { Pump(); Thread.Sleep(10); }
            Require(condition(), message);
            if (delayed) Console.WriteLine($"  Window chrome settled after {elapsed.ElapsedMilliseconds} ms: state {window.WindowState}, size {window.ActualWidth}x{window.ActualHeight}.");
        }
    }

    private static int HitTest(nint handle, Point point) => (int)SendMessage(handle, 0x84, 0, Pack(point));
    private static nint Pack(Point point) => (nint)(((int)point.X & 0xffff) | (((int)point.Y & 0xffff) << 16));
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
