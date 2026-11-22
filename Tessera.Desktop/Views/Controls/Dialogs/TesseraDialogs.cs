using System.Windows;
using Tessera.WpfStyles.Controls;

namespace Tessera.Desktop;

internal static class TesseraDialogs
{
    public static TesseraDialog Create(string message, string title, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None, string? affirmativeText = null, string? negativeText = null,
        bool confirmByDefault = true)
    {
        var labels = new TesseraDialogLabels(Localization.Text("Ok"), affirmativeText ?? Localization.Text("Yes"),
            negativeText ?? Localization.Text("No"), Localization.Text("Cancel"), Localization.Text("CloseDialog"));
        var dialog = new TesseraDialog(message, title, buttons, image, labels, confirmByDefault);
        dialog.Resources.MergedDictionaries.Add(new AppearanceResources());
        return dialog;
    }

    public static MessageBoxResult Show(Window? owner, string message, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None,
        string? affirmativeText = null, string? negativeText = null, bool confirmByDefault = true)
    {
        var dialog = Create(message, title, buttons, image, affirmativeText, negativeText, confirmByDefault);
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
        return dialog.Result;
    }

    public static MessageBoxResult Show(string message, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None) =>
        Show(Application.Current?.MainWindow, message, title, buttons, image);
}
