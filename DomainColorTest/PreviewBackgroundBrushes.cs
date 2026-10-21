using System.Windows;
using System.Windows.Media;
using Tessera.WpfStyles.Themes;

namespace DomainColorTest;

/// <summary>Theme colours shared by the image, animation and tile preview surfaces.</summary>
internal static class PreviewBackgroundBrushes
{
    public static SolidColorBrush Resource(FrameworkElement owner, string key)
        => (owner.TryFindResource(key) ?? AppearanceResources.Shared[key]) as SolidColorBrush ?? Brushes.Gray;

    public static SolidColorBrush Solid(FrameworkElement owner, bool light)
    {
        bool lightTheme = ThemeManager.Current == TesseraTheme.Light;
        return Resource(owner, light ? lightTheme ? "Ts.Raised" : "Ts.Text" : lightTheme ? "Ts.Text" : "Ts.Canvas");
    }

    public static Brush Create(FrameworkElement owner, int background)
    {
        bool light = background is 2 or 3;
        var first = Solid(owner, light);
        if (background is 1 or 2) return first;
        bool lightTheme = ThemeManager.Current == TesseraTheme.Light;
        var second = Resource(owner, light ? lightTheme ? "Ts.Panel" : "Ts.TextSecondary" : lightTheme ? "Ts.TextSecondary" : "Ts.Panel");
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(first, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        group.Children.Add(new GeometryDrawing(second, null, new RectangleGeometry(new Rect(0, 0, 12, 12))));
        group.Children.Add(new GeometryDrawing(second, null, new RectangleGeometry(new Rect(12, 12, 12, 12))));
        var checker = new DrawingBrush(group) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 24, 24), Stretch = Stretch.None };
        checker.Freeze();
        return checker;
    }
}
