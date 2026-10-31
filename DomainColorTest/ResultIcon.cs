using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace DomainColorTest;

public sealed class ResultIcon : FrameworkElement
{
    private static readonly Brush MainBrush = new SolidColorBrush(Color.FromRgb(0xe9, 0xee, 0xf2));
    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x64, 0xd8, 0xff));
    static ResultIcon() { MainBrush.Freeze(); AccentBrush.Freeze(); }
    public static readonly DependencyProperty OperationIdProperty = DependencyProperty.Register(nameof(OperationId), typeof(string), typeof(ResultIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = Control.ForegroundProperty.AddOwner(typeof(ResultIcon),
        new FrameworkPropertyMetadata(MainBrush, FrameworkPropertyMetadataOptions.AffectsRender));


    private static readonly HashSet<string> SupportedOperations =
    [
        "image.resize", "image.trim", "image.colors", "image.cleanup",
        "grid.align", "grid.align.local", "grid.align.local.snapped", "grid.reduce",
        "filter.monochrome", "filter.sharpen", "filter.ascii", "color.replace",
        "color.channel.adjust", "background.remove", "image.finalist.reduce",
        "macro.apply", "manual.erase"
    ];
    private static readonly Dictionary<string, IconShape[]> Shapes = [];

    public string? OperationId { get => (string?)GetValue(OperationIdProperty); set => SetValue(OperationIdProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public static bool Supports(string? operationId) => operationId is not null && SupportedOperations.Contains(operationId);

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        double size = Math.Min(ActualWidth, ActualHeight);
        context.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        context.PushTransform(new ScaleTransform(size / 24, size / 24));
        if (!Supports(OperationId))
        {
            var pen = new Pen(Foreground, 1.7) { LineJoin = PenLineJoin.Round };
            context.DrawRoundedRectangle(null, pen, new Rect(4, 3, 16, 18), 1, 1);
            context.DrawLine(pen, new Point(8, 9), new Point(16, 9));
            context.DrawLine(pen, new Point(8, 13), new Point(16, 13));
            context.DrawLine(pen, new Point(8, 17), new Point(13, 17));
        }
        else foreach (var shape in ReadShapes(OperationId!))
        {
            Brush color = shape.Accent ? AccentBrush : Foreground;
            Pen? pen = shape.Stroke ? new Pen(color, 1.7)
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round,
                DashStyle = shape.Dashes is null ? DashStyles.Solid : new DashStyle(shape.Dashes, 0)
            } : null;
            context.DrawGeometry(shape.Fill ? color : null, pen, shape.Geometry);
        }
        context.Pop(); context.Pop();
    }

    private static IconShape[] ReadShapes(string operationId)
    {
        if (Shapes.TryGetValue(operationId, out var existing)) return existing;

        using var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/DomainColorTest;component/Assets/ResultIcons/{operationId}.svg"))!.Stream;
        var document = XDocument.Load(stream);
        var result = new List<IconShape>();
        foreach (var element in document.Root!.Descendants())
        {
            Geometry? geometry = element.Name.LocalName switch
            {
                "path" => Geometry.Parse((string)element.Attribute("d")!),
                "rect" => new RectangleGeometry(new Rect(Number(element, "x"), Number(element, "y"), Number(element, "width"), Number(element, "height"))),
                "circle" => new EllipseGeometry(new Point(Number(element, "cx"), Number(element, "cy")), Number(element, "r"), Number(element, "r")),
                _ => null
            };
            if (geometry is null) continue;

            bool accent = ((string?)element.Attribute("class"))?.Contains("accent", StringComparison.Ordinal) == true;
            string? fillText = (string?)element.Attribute("fill");
            double[]? dashes = element.Attribute("stroke-dasharray") is { } dash
                ? dash.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray() : null;
            geometry.Freeze();
            result.Add(new IconShape(geometry, accent, fillText is not null && fillText != "none", (string?)element.Attribute("stroke") != "none", dashes));
        }
        Shapes.Add(operationId, [.. result]);
        return Shapes[operationId];
    }

    private static double Number(XElement element, string name) => double.Parse((string)element.Attribute(name)!, CultureInfo.InvariantCulture);
    private sealed record IconShape(Geometry Geometry, bool Accent, bool Fill, bool Stroke, double[]? Dashes);
}
