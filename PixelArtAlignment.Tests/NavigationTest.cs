using System.Windows;
using System.Windows.Controls;
using DomainColorTest;

namespace PixelArtAlignment.Tests;

internal static class NavigationTest
{
    public static void Click(FrameworkElement owner, string name)
    {
        var element = (FrameworkElement)owner.FindName(name);
        if (element is MenuItem item)
        {
            if (item.HasItems) item.IsSubmenuOpen = !item.IsSubmenuOpen;
            else item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
        }
        else ((Button)element).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    public static Choice Select(MainWindow window, string property) => new(window, property);
    internal sealed class Choice(MainWindow window, string property)
    {
        public int SelectedIndex
        {
            get => (int)typeof(MainWindow).GetProperty(property)!.GetValue(window)!;
            set => typeof(MainWindow).GetProperty(property)!.SetValue(window, value);
        }
    }
}
