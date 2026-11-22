using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tessera.Desktop;

namespace Tessera.RegressionTests;

internal static class UninstallChecks
{
    public static void Run()
    {
        Exception? uiError = null;
        var ui = new Thread(() => { try { VerifySettings(); } catch (Exception ex) { uiError = ex; } }) { IsBackground = true };
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        Require(ui.Join(TimeSpan.FromSeconds(20)), "Uninstall settings test timed out");
        if (uiError is not null) throw uiError;

        string root = Path.GetFullPath(Path.Combine("artifacts", "uninstall-verification", Guid.NewGuid().ToString("N"), "Tessera's [fixture]"));
        string local = Path.Combine(root, "Local");
        string temp = Path.Combine(root, "Temp");
        string tessera = Path.Combine(local, "Tessera");
        string legacy = Path.Combine(local, "Pixelizator");
        string temporary = Path.Combine(temp, "Tessera");
        string bundle = Path.Combine(temp, ".net", "Tessera");
        string customBundleBase = Path.Combine(root, "bundle cache");
        string renamedBundle = Path.Combine(customBundleBase, "Renamed Tessera", "bundle-id");
        string install = Path.Combine(tessera, "bin");
        string executable = Path.Combine(install, "Renamed Tessera.exe");
        string portable = Path.Combine(install, "WorkspaceData", "Library");
        string external = Path.Combine(root, "External Library");
        string unrelated = Path.Combine(local, "Unrelated");
        foreach (string folder in new[] { legacy, temporary, bundle, renamedBundle, portable, external, unrelated })
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "test.txt"), "fixture");
        }
        File.WriteAllText(Path.Combine(portable, ".copied-library"), "fixture");
        string orphan = Path.Combine(temp, "tessera-map-" + Guid.NewGuid().ToString("N") + ".png");
        string unrelatedTemp = Path.Combine(temp, "tessera-map-keep.png");
        File.WriteAllText(orphan, "fixture"); File.WriteAllText(unrelatedTemp, "keep");
        File.Copy(typeof(TesseraUninstaller).Assembly.Location, executable);
        var plan = TesseraUninstaller.Prepare(executable, external, local, temp, customBundleBase, renamedBundle);
        Require(plan.Directories.ToHashSet().SetEquals([tessera, legacy, temporary, bundle, renamedBundle]) &&
            plan.Files.SequenceEqual([orphan]) && plan.Executable == executable, "Uninstall selected an unexpected path");

        string otherInstall = Path.Combine(root, "Other Installation");
        string otherExecutable = Path.Combine(otherInstall, "Tessera.exe");
        string otherLibrary = Path.Combine(otherInstall, "WorkspaceData", "Library");
        Directory.CreateDirectory(otherLibrary);
        File.Copy(executable, otherExecutable);
        File.WriteAllText(Path.Combine(otherLibrary, "workspace.json"), "{}");
        Require(!TesseraUninstaller.Prepare(otherExecutable, external, local, temp).Directories.Contains(otherLibrary),
            "A generic neighboring library was marked for deletion");
        File.WriteAllText(Path.Combine(otherLibrary, ".copied-library"), "fixture");
        Require(TesseraUninstaller.Prepare(otherExecutable, external, local, temp).Directories.Contains(otherLibrary),
            "The old portable Tessera library was not detected");

        var keeper = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 20" }) keeper.ArgumentList.Add(arg);
        using var owner = Process.Start(keeper)!;
        try
        {
            File.SetAttributes(executable, FileAttributes.ReadOnly);
            using var readOnly = Schedule();
            Thread.Sleep(400);
            Require(!readOnly.HasExited && Directory.Exists(temporary), "Uninstall acted before the app exited");
            owner.Kill(); owner.WaitForExit();
            Require(readOnly.WaitForExit(15000) && readOnly.ExitCode == 1 && Directory.Exists(temporary) && File.Exists(executable),
                "Uninstall did not leave all paths intact for a read-only executable");
            File.SetAttributes(executable, FileAttributes.Normal);
            using var helper = Schedule();
            Require(helper.WaitForExit(15000), "Uninstall helper timed out");
            Require(helper.ExitCode == 0, "Uninstall helper failed: " + helper.StandardError.ReadToEnd());
            Require(!File.Exists(executable) && !File.Exists(orphan) && plan.Directories.All(path => !Directory.Exists(path)),
                "Uninstall left app files behind");
            Require(File.Exists(Path.Combine(external, "test.txt")) && File.Exists(Path.Combine(unrelated, "test.txt")) && File.Exists(unrelatedTemp),
                "Uninstall removed files outside the plan");
        }
        finally
        {
            if (!owner.HasExited) owner.Kill();
        }

        Process Schedule()
        {
            var start = TesseraUninstaller.CreateStartInfo(plan, owner, showErrorDialog: false);
            start.RedirectStandardError = true;
            return Process.Start(start)!;
        }
    }

    private static void VerifySettings()
    {
        var settings = new AppearanceWindow(Tessera.WpfStyles.Themes.TesseraTheme.Dark, _ => false);
        var button = (Button)settings.FindName("uninstallButton");
        Require(ReferenceEquals(((Panel)button.Parent).Children[^1], button) &&
            button.Background is SolidColorBrush { Color: { R: > 140, G: < 60, B: < 60 } }, "The red uninstall button is missing from the bottom of Settings");
        var dialog = new TesseraDialog("Fixture", "Uninstall", MessageBoxButton.YesNo, confirmByDefault: false);
        var actions = ((Panel)dialog.FindName("dialogButtons")).Children.OfType<Button>().ToArray();
        Require(actions.Single(action => action.IsDefault).Tag is MessageBoxResult.No, "Enter would confirm uninstall by default");
        dialog.Close(); settings.Close();
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
