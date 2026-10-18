using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace DomainColorTest;

/// <summary>Renders the bundled stroke-only SVG with the current button foreground.</summary>
public sealed class SettingsIcon : FrameworkElement
{
    public static readonly DependencyProperty ForegroundProperty = Control.ForegroundProperty.AddOwner(typeof(SettingsIcon),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly Geometry Gear = ReadGeometry();
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    private static Geometry ReadGeometry()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/DomainColorTest;component/Assets/settings.svg"))!.Stream;
        var svg = XDocument.Load(stream);
        var geometry = Geometry.Parse(svg.Root!.Element(svg.Root.Name.Namespace + "path")!.Attribute("d")!.Value);
        geometry.Freeze(); return geometry;
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        double size = Math.Min(ActualWidth, ActualHeight);
        context.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        context.PushTransform(new ScaleTransform(size / 24, size / 24));
        var pen = new Pen(Foreground, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        context.DrawGeometry(null, pen, Gear);
        context.Pop(); context.Pop();
    }
}
