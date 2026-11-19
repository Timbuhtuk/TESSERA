using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace Tessera.Desktop;

public sealed class WorkspaceAssetIcon : FrameworkElement
{
    private static readonly Lazy<Dictionary<EWorkspaceAssetKind, Geometry>> Icons = new(() =>
        Enum.GetValues<EWorkspaceAssetKind>().ToDictionary(kind => kind, kind =>
        {
            string name = kind.ToString().ToLowerInvariant();
            var uri = new Uri($"pack://application:,,,/Tessera.Desktop;component/Assets/WorkspaceAssetIcons/{name}.svg");
            using var stream = Application.GetResourceStream(uri)!.Stream;
            var svg = XElement.Load(stream);
            var geometry = Geometry.Parse(svg.Element(svg.Name.Namespace + "path")!.Attribute("d")!.Value);
            geometry.Freeze();
            return geometry;
        }));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind),
        typeof(EWorkspaceAssetKind), typeof(WorkspaceAssetIcon),
        new FrameworkPropertyMetadata(EWorkspaceAssetKind.Image, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = Control.ForegroundProperty.AddOwner(typeof(WorkspaceAssetIcon),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public EWorkspaceAssetKind Kind { get => (EWorkspaceAssetKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if (!Icons.Value.TryGetValue(Kind, out var geometry)) geometry = Icons.Value[EWorkspaceAssetKind.Image];
        double size = Math.Min(ActualWidth, ActualHeight);
        var pen = new Pen(Foreground, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        context.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        context.PushTransform(new ScaleTransform(size / 24, size / 24));
        context.DrawGeometry(null, pen, geometry);
        context.Pop();
        context.Pop();
    }
}
