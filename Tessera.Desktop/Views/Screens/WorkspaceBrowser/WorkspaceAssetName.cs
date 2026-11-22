using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Tessera.Desktop;

public sealed class WorkspaceAssetName : Decorator
{
    private const string Ellipsis = "...";
    private readonly TextBlock _label = new()
    {
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.NoWrap,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight
    };

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text),
        typeof(string), typeof(WorkspaceAssetName), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty AllowSecondLineProperty = DependencyProperty.Register(nameof(AllowSecondLine),
        typeof(bool), typeof(WorkspaceAssetName), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public WorkspaceAssetName() => Child = _label;

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool AllowSecondLine { get => (bool)GetValue(AllowSecondLineProperty); set => SetValue(AllowSecondLineProperty, value); }

    protected override Size MeasureOverride(Size constraint)
    {
        _label.LineHeight = AllowSecondLine ? 15 : 16;
        _label.Text = FitName(constraint.Width);
        return base.MeasureOverride(constraint);
    }

    private string FitName(double width)
    {
        string name = Text?.ReplaceLineEndings(" ") ?? "";
        if (name.Length == 0 || double.IsPositiveInfinity(width)) return name;
        if (width <= 0) return "";

        var typeface = new Typeface(_label.FontFamily, _label.FontStyle, _label.FontWeight, _label.FontStretch);
        var formatting = TextOptions.GetTextFormattingMode(_label);
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (Fits(name)) return name;
        if (!Fits(Ellipsis)) return "";
        if (!AllowSecondLine) return Prefix(name, Ellipsis) + Ellipsis;

        string firstLine = Prefix(name, "");
        if (firstLine.Length == 0) return Ellipsis;
        string lastLine = name[firstLine.Length..].TrimStart();
        if (!Fits(lastLine)) lastLine = Prefix(lastLine, Ellipsis).TrimEnd() + Ellipsis;
        return firstLine.TrimEnd() + "\n" + lastLine;

        bool Fits(string text) => new FormattedText(text, CultureInfo.CurrentUICulture, _label.FlowDirection,
            typeface, _label.FontSize, Brushes.Black, null, formatting, pixelsPerDip).WidthIncludingTrailingWhitespace <= width;

        string Prefix(string text, string suffix)
        {
            int[] starts = StringInfo.ParseCombiningCharacters(text);
            int first = 0, last = starts.Length;
            while (first < last)
            {
                int middle = (first + last + 1) / 2;
                int end = middle == starts.Length ? text.Length : starts[middle];
                if (Fits(text[..end] + suffix)) first = middle;
                else last = middle - 1;
            }
            int length = first == starts.Length ? text.Length : starts[first];
            return text[..length];
        }
    }
}
