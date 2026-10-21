using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelArtDownscale;
using Bitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingRectangle = System.Drawing.Rectangle;

namespace DomainColorTest;

public partial class MainWindow
{
    private PalettePreviewTooltips? _palettePreviews;

    private void InitializePalettePreviews()
    {
        _palettePreviews = new PalettePreviewTooltips();
        _palettePreviews.Attach(paletteInput, index => new ColorPreviewSettings(
            (PaletteKind)(index + 1), paletteStepInput.Value, ditheringInput.IsChecked == true));
        _palettePreviews.Attach(quantizationInput, index => new ColorPreviewSettings(
            PaletteKind.None, Quantization: (QuantizationMethod)index,
            ColorCount: quantizationColorsInput.Value, UseColorWeights: colorWeightsInput.IsChecked == true));
        paletteStepInput.ValueChanged += (_, _) => _palettePreviews.RefreshOpenTooltips();
        quantizationColorsInput.ValueChanged += (_, _) => _palettePreviews.RefreshOpenTooltips();
        foreach (var check in new[] { ditheringInput, colorWeightsInput })
        {
            check.Checked += (_, _) => _palettePreviews.RefreshOpenTooltips();
            check.Unchecked += (_, _) => _palettePreviews.RefreshOpenTooltips();
        }
        Closed += (_, _) => _palettePreviews.Close();
    }
}

/// <summary>Options include their real settings so an open preview never shows a previous step/count.</summary>
internal readonly record struct ColorPreviewSettings(PaletteKind Palette, int Step = 16, bool Dithering = false,
    QuantizationMethod? Quantization = null, int ColorCount = 64, bool UseColorWeights = false)
{
    public ColorPreviewSettings Normalize() => Quantization.HasValue
        ? this with { Palette = PaletteKind.None, Step = 16, Dithering = false }
        : this with { Step = Palette == PaletteKind.Step ? Step : 16,
            Dithering = Palette is >= PaletteKind.DB16 and <= PaletteKind.GameBoy && Dithering,
            ColorCount = 64, UseColorWeights = false };

    public string Label => Quantization.HasValue
        ? Localization.Format("{0} · {1} цветов", Quantization.Value switch
        {
            QuantizationMethod.MedianCut => "Median Cut",
            QuantizationMethod.KMeansLab => "K-Means LAB",
            _ => "K-Means Linear"
        }, ColorCount)
        : Palette == PaletteKind.Step ? Localization.Format("Шаг · {0}", Step)
            : Dithering ? Localization.Format("{0} · дизеринг", Palette) : Palette.ToString();
}

/// <summary>One background renderer, cancellable waits, and a bounded cache of frozen comparisons.</summary>
internal sealed class PalettePreviewTooltips
{
    private const int CacheLimit = 24;
    private readonly Dictionary<ColorPreviewSettings, BitmapSource> _cache = new();
    private readonly Queue<ColorPreviewSettings> _cacheOrder = new();
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly List<PreviewTip> _tips = new();
    private bool _closed;

    public void Attach(ComboBox combo, Func<int, ColorPreviewSettings> settings)
    {
        AttachTip(combo, () => settings(Math.Max(0, combo.SelectedIndex)).Normalize());
        for (int q = 0; q < combo.Items.Count; q++)
        {
            int index = q;
            if (combo.Items[q] is ComboBoxItem item)
                AttachTip(item, () => settings(index).Normalize());
        }
        combo.SelectionChanged += (_, _) => RefreshOpenTooltips();
        // The parent tooltip belongs to the closed selector; each dropdown row has its own example.
        combo.DropDownOpened += (_, _) =>
        {
            var tip = _tips.First(preview => ReferenceEquals(preview.Owner, combo));
            tip.ToolTip.IsOpen = false;
        };
    }

    private void AttachTip(FrameworkElement owner, Func<ColorPreviewSettings> settings)
    {
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) };
        var image = new Image { Width = 360, Height = 200, Stretch = Stretch.None };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var loading = new TextBlock
        {
            Text = Localization.Text("Подготовка примера…"), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var originalLabel = ImageLabel(Localization.Text("Исходник"), HorizontalAlignment.Left);
        var resultLabel = ImageLabel("", HorizontalAlignment.Right);
        var comparison = new Grid { Width = 360, Height = 200 };
        comparison.Children.Add(image);
        comparison.Children.Add(loading);
        comparison.Children.Add(originalLabel);
        comparison.Children.Add(resultLabel);
        var frame = new Border { BorderThickness = new Thickness(1), Child = comparison };
        frame.SetResourceReference(Border.BorderBrushProperty, "Ts.Border");
        var description = new TextBlock
        {
            Text = Localization.Text("Эталонный пример, не текущий документ. Слева — исходник, справа — обработка."),
            TextWrapping = TextWrapping.Wrap, Width = 360, Margin = new Thickness(0, 7, 0, 0), FontSize = 11
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "Ts.TextSecondary");
        var panel = new StackPanel();
        panel.Children.Add(title);
        panel.Children.Add(frame);
        panel.Children.Add(description);
        var toolTip = new ToolTip { Content = panel, Placement = PlacementMode.Right, Padding = new Thickness(10) };
        toolTip.SetResourceReference(Control.BackgroundProperty, "Ts.Raised");
        toolTip.SetResourceReference(Control.ForegroundProperty, "Ts.Text");
        toolTip.SetResourceReference(Control.BorderBrushProperty, "Ts.BorderStrong");
        toolTip.SetResourceReference(Control.FontFamilyProperty, "UiFont");
        var preview = new PreviewTip(owner, toolTip, title, image, loading, resultLabel, settings);
        _tips.Add(preview);
        owner.ToolTip = toolTip;
        ToolTipService.SetInitialShowDelay(owner, 350);
        ToolTipService.SetShowDuration(owner, 30000);
        ToolTipService.SetShowOnDisabled(owner, true);
        toolTip.Opened += (_, _) => _ = RefreshAsync(preview);
        toolTip.Closed += (_, _) => preview.Pending?.Cancel();
    }

    private static TextBlock ImageLabel(string text, HorizontalAlignment alignment) => new()
    {
        Text = text, HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(7), Padding = new Thickness(5, 3, 5, 3), FontSize = 11,
        Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(220, 12, 12, 12))
    };

    public void RefreshOpenTooltips()
    {
        if (_closed) return;
        foreach (var preview in _tips)
            if (preview.ToolTip.IsOpen) _ = RefreshAsync(preview);
    }

    private async Task RefreshAsync(PreviewTip preview)
    {
        preview.Pending?.Cancel();
        using var pending = new CancellationTokenSource();
        preview.Pending = pending;
        var token = pending.Token;
        try
        {
            var settings = preview.Settings();
            preview.Title.Text = settings.Label;
            preview.ResultLabel.Text = settings.Quantization.HasValue ? Localization.Text("Результат") : settings.Label;
            preview.Loading.Text = Localization.Text("Подготовка примера…");
            preview.Loading.Visibility = Visibility.Visible;
            preview.Image.Source = null;
            // Spinner/keyboard edits replace queued work before it acquires the renderer.
            await Task.Delay(120, token);
            var comparison = await Task.Run(async () =>
            {
                await _renderGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (_cache.TryGetValue(settings, out var cached)) return cached;
                    var generated = Generate(settings);
                    if (_cache.Count == CacheLimit) _cache.Remove(_cacheOrder.Dequeue());
                    _cache.Add(settings, generated);
                    _cacheOrder.Enqueue(settings);
                    return generated;
                }
                finally { _renderGate.Release(); }
            }, token);
            if (_closed || token.IsCancellationRequested || !preview.ToolTip.IsOpen || preview.Settings() != settings) return;
            preview.Image.Source = comparison;
            preview.Loading.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && !_closed)
            {
                preview.Loading.Text = Localization.Text("Не удалось подготовить пример.");
                System.Diagnostics.Debug.WriteLine("Color option preview: " + ex);
            }
        }
        finally
        {
            if (ReferenceEquals(preview.Pending, pending)) preview.Pending = null;
        }
    }

    public void Close()
    {
        _closed = true;
        foreach (var preview in _tips)
        {
            preview.Pending?.Cancel();
            preview.ToolTip.IsOpen = false;
            preview.Image.Source = null;
        }
    }

    /// <summary>Deterministic QA entry point. Both halves come from the real standalone color pipeline.</summary>
    internal static BitmapSource Generate(ColorPreviewSettings settings)
    {
        using var source = CreateSample();
        using var result = IndependentImageProcessor.ApplyColors(source, new DownscaleOptions
        {
            IndependentColorMode = settings.Quantization.HasValue ? IndependentColorMode.Quantization : IndependentColorMode.Palette,
            Palette = settings.Palette, PaletteStep = settings.Step, EnableDithering = settings.Dithering,
            Quantization = settings.Quantization ?? QuantizationMethod.KMeansLab,
            QuantizationColors = settings.ColorCount, UseColorWeights = settings.UseColorWeights, ThreadCount = 1
        });
        using var comparison = new Bitmap(360, 200, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < comparison.Height; y++)
        {
            double boundary = 239 - y * 118.0 / (comparison.Height - 1);
            for (int x = 0; x < comparison.Width; x++)
            {
                int sx = x / 2, sy = y / 2;
                var color = x < boundary ? source.GetPixel(sx, sy) : result.GetPixel(sx, sy);
                if (Math.Abs(x - boundary) <= 1) color = DrawingColor.White;
                else if (Math.Abs(x - boundary) <= 2) color = DrawingColor.Black;
                comparison.SetPixel(x, y, color);
            }
        }
        return PixelPreview.ToBitmapSource(comparison);
    }

    /// <summary>Bundled landscape plus a color/neutral calibration band; independent of open documents.</summary>
    internal static Bitmap CreateSample()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/DomainColorTest;component/Assets/landscape.png"))!.Stream;
        using var landscape = new Bitmap(stream);
        var sample = new Bitmap(180, 100, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(sample))
        {
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.DrawImage(landscape, new DrawingRectangle(0, 0, 180, 80));
        }
        for (int y = 0; y < sample.Height; y++)
            for (int x = 0; x < sample.Width; x++)
            {
                DrawingColor color;
                if (y < 80)
                {
                    double light = sample.GetPixel(x, y).R / 255.0;
                    double hue = y < 38 ? 228 - y * 1.2 : 155 + (y - 38) * 1.6;
                    color = Hsv(hue, .38 + .2 * (1 - light), light);
                }
                else if (y < 94)
                    color = Hsv(x * 360.0 / 180, .9, .38 + .62 * (y - 80) / 13);
                else
                {
                    int level = (int)Math.Round(x * 255.0 / 179);
                    color = DrawingColor.FromArgb(level, level, level);
                }
                sample.SetPixel(x, y, color);
            }
        return sample;
    }

    private static DrawingColor Hsv(double hue, double saturation, double value)
    {
        double chroma = value * saturation;
        double second = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        double offset = value - chroma;
        var rgb = (int)(hue / 60) switch
        {
            0 => (chroma, second, 0.0), 1 => (second, chroma, 0.0), 2 => (0.0, chroma, second),
            3 => (0.0, second, chroma), 4 => (second, 0.0, chroma), _ => (chroma, 0.0, second)
        };
        return DrawingColor.FromArgb((int)Math.Round((rgb.Item1 + offset) * 255),
            (int)Math.Round((rgb.Item2 + offset) * 255), (int)Math.Round((rgb.Item3 + offset) * 255));
    }

    private sealed record PreviewTip(FrameworkElement Owner, ToolTip ToolTip, TextBlock Title, Image Image,
        TextBlock Loading, TextBlock ResultLabel, Func<ColorPreviewSettings> Settings)
    {
        public CancellationTokenSource? Pending { get; set; }
    }
}
