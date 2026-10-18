using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DomainColorTest;

internal static class WorkspaceBrushSafety
{
    private static bool _registered;
    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        // Disabled and theme triggers can replace an animated brush with a frozen resource.
        // Class handlers run before the style library's instance hover handlers.
        EventManager.RegisterClassHandler(typeof(Button), Mouse.MouseEnterEvent, new MouseEventHandler(Prepare), true);
        EventManager.RegisterClassHandler(typeof(Button), Mouse.MouseLeaveEvent, new MouseEventHandler(Prepare), true);
    }
    private static void Prepare(object sender, MouseEventArgs e)
    {
        if (sender is not Button button || !Tessera.WpfStyles.Motion.HoverMotion.GetIsEnabled(button)) return;
        if (button.Background is SolidColorBrush { IsFrozen: true } background) button.SetCurrentValue(Control.BackgroundProperty, background.CloneCurrentValue());
        if (button.BorderBrush is SolidColorBrush { IsFrozen: true } border) button.SetCurrentValue(Control.BorderBrushProperty, border.CloneCurrentValue());
    }
}
