using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Tessera.Desktop;

namespace Tessera.RegressionTests;

internal static class WorkspaceExportChecks
{
    public static void Run()
    {
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "workspace-exports-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string first = Path.Combine(folder, "first.png"), second = Path.Combine(folder, "second.png"), third = Path.Combine(folder, "third.png");
        using var image = new Bitmap(8, 8);
        using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.White);
        image.Save(first); image.SetPixel(3, 3, Color.Red); image.Save(second); image.SetPixel(4, 4, Color.Blue); image.Save(third);
        var originals = new[] { first, second, third }.ToDictionary(path => path, File.ReadAllBytes);
        var window = new MainWindow(Path.Combine(folder, "library")) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show(); Pump();
            Invoke(window, "LoadImage", first); Invoke(window, "LoadImage", second);
            var background = (BackgroundWorkspace)window.FindControl("backgroundWorkspace");
            background.SetImage(image, "third.png", third); Await(background.RefreshPreviewAsync());
            foreach (string path in originals.Keys) Reject(() => Await(background.SaveAsync(path.ToUpperInvariant(), true)));
            string project = Path.Combine(folder, "existing.tboard"); File.WriteAllText(project, "keep project");
            Reject(() => Await(background.SaveAsync(project, true)));
            Require(File.ReadAllText(project) == "keep project", "Background export changed a project file");
            var library = Get<ImageLibrary>(window, "_library");
            Reject(() => Await(background.SaveAsync(Path.Combine(library.DirectoryPath, "new.png"), true)));
            string output = Path.Combine(folder, "background.PNG");
            Await(background.SaveAsync(output)); Await(background.SaveAsync(output, true));
            using (var saved = new Bitmap(output)) Require(saved.GetPixel(0, 0).A == 0, "Safe background export lost transparency");
            // Protection also applies in the reverse direction, from a standalone workspace to the editor.
            ((IntegerInput)window.FindControl("widthInput")).Value = 4;
            ((IntegerInput)window.FindControl("heightInput")).Value = 4;
            ((Button)window.FindControl("scaleOnlyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitUntil(() => !Get<bool>(window, "_processing"));
            Reject(() => Invoke(window, "SaveResult", third));

            var icons = (IconWorkspace)window.FindControl("iconWorkspace");
            icons.SetImage(image, "third.png", third);
            Reject(() => Await(icons.SaveAsync(Path.Combine(library.DirectoryPath, "new.ico"), true)));
            Reject(() => Await(icons.SaveAsync(Path.Combine(folder, "wrong.png"), true)));
            Await(icons.SaveAsync(Path.Combine(folder, "safe.ico")));

            var board = (MoodboardWorkspace)window.FindControl("moodboardWorkspace");
            ((Button)board.FindName("createButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Reject(() => board.ExportToPng(second));
            Reject(() => board.SaveToFile(Path.Combine(library.DirectoryPath, "board.tboard")));
            board.SaveToFile(Path.Combine(folder, "board.tboard"));

            foreach (var pair in originals) Require(File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value), "Export changed original " + pair.Key);
            Require(File.ReadAllText(project) == "keep project", "Export changed unrelated project");
            Require(!Directory.EnumerateFiles(folder, "*.tmp", SearchOption.AllDirectories).Any(), "Rejected export left temporary files");
            Console.WriteLine("  Workspace exports: source/library protection, image formats, repeat exports and unchanged bytes passed.");
        }
        finally { window.Close(); Pump(); }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException or IOException or ArgumentException) { return; }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException) { return; }
        throw new Exception("Protected path or wrong extension accepted");
    }
    private static T Get<T>(object target, string name) => (T)WindowTestAccess.GetField(target, name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static object? Invoke(object target, string name, params object[] args) => WindowTestAccess.Invoke(target, name, BindingFlags.Instance | BindingFlags.NonPublic, args);
    private static void Await(Task task) { WaitUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); Pump(); }
    private static void WaitUntil(Func<bool> condition)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(20);
        while (!condition() && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        Require(condition(), "Workspace export timed out"); Pump();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
