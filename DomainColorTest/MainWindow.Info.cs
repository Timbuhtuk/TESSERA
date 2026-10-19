using System.Collections.ObjectModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PixelArtDownscale;
using Bitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;

namespace DomainColorTest;

public partial class MainWindow
{
    private readonly ObservableCollection<PaletteColorEntry> _infoPalette = [];
    private ColorReplaceWindow? _replaceWindow;
    private Bitmap? _infoSourceShown;
    private Bitmap? _infoResultShown;
    private Bitmap? _infoBasisShown;
    private long _infoRefreshVersion;
    private bool _infoSnapshotReady;
    private bool InfoToolVisible => _toolWindows.ContainsKey("info") ||
        _workspaceDock?.Visibility == Visibility.Visible && _workspaceGroups.Contains(infoToolGroup);

    private void InitializeInfo()
    {
        infoPaletteList.ItemsSource = _infoPalette;
        infoManualReplaceButton.Click += (_, _) => OpenColorReplace(DrawingColor.Black, true);
    }

    private void ResetInfoSnapshot()
    {
        _infoRefreshVersion++;
        _infoSnapshotReady = false;
        _replaceWindow?.Close();
        _infoSourceShown = _infoResultShown = _infoBasisShown = null;
        _infoPalette.Clear();
    }

    private async void RefreshInfo()
    {
        if (_processing || !InfoToolVisible) return;
        Bitmap? source = _sourceImage;
        Bitmap? result = _downscaledResult;
        Bitmap? basis = ProcessingBasis == 1 && result is not null ? result : source;
        if (_infoSnapshotReady && ReferenceEquals(source, _infoSourceShown) &&
            ReferenceEquals(result, _infoResultShown) &&
            ReferenceEquals(basis, _infoBasisShown)) return;

        if (!ReferenceEquals(basis, _infoBasisShown)) _replaceWindow?.Close();
        _infoSnapshotReady = true;
        _infoSourceShown = source;
        _infoResultShown = result;
        _infoBasisShown = basis;
        long version = ++_infoRefreshVersion;
        infoSourceSummary.Text = source is null ? Localization.Text("Нет изображения") : Localization.Format("{0} × {1} px · подсчёт цветов…", source.Width, source.Height);
        infoResultSummary.Text = result is null ? Localization.Text("Пока нет результата") : Localization.Format("{0} × {1} px · подсчёт цветов…", result.Width, result.Height);
        infoBasisLabel.Text = ReferenceEquals(basis, result) && result is not null
            ? Localization.Text("Цвета выбранного результата") : Localization.Text("Цвета исходника");
        infoPaletteStatus.Text = basis is null ? Localization.Text("Откройте изображение") : Localization.Text("Подсчёт видимых цветов…");
        _infoPalette.Clear();
        infoSourceDetails.Text = infoResultDetails.Text = "";
        infoManualReplaceButton.Visibility = Visibility.Collapsed;
        if (basis is null) return;

        using var sourceCopy = source is null ? null : (Bitmap)source.Clone();
        using var resultCopy = result is null ? null : (Bitmap)result.Clone();
        try
        {
            var sourceFileSize = InfoFileSize(_activeSource is null ? null : _library.SourcePath(_activeSource));
            var resultFileSize = InfoFileSize(_activeSource?.SelectedGeneration is { } generation ? _library.ResultPath(_activeSource, generation) : null);
            var summary = await Task.Run(() =>
            {
                var sourceInfo = sourceCopy is null ? null : ReadInfoSummary(sourceCopy);
                var resultInfo = resultCopy is null ? null : ReadInfoSummary(resultCopy);
                var selected = ReferenceEquals(basis, result) ? resultInfo! : sourceInfo!;
                return (sourceInfo, resultInfo, count: selected.Colors, palette: selected.Palette);
            });
            if (version != _infoRefreshVersion || !InfoToolVisible) return;

            if (summary.sourceInfo is { } input)
            {
                infoSourceSummary.Text = Localization.Format("{0} × {1} px · {2:N0} видимых цветов", input.Width, input.Height, input.Colors);
                infoSourceDetails.Text = FormatInfoDetails(input, sourceFileSize);
            }
            if (summary.resultInfo is { } output)
            {
                infoResultSummary.Text = Localization.Format("{0} × {1} px · {2:N0} видимых цветов", output.Width, output.Height, output.Colors);
                infoResultDetails.Text = FormatInfoDetails(output, resultFileSize);
            }
            infoPaletteStatus.Text = summary.count > ImageColorTools.MaxPaletteColors
                ? Localization.Format("Цветов: {0:N0}. Список доступен до {1} цветов.", summary.count, ImageColorTools.MaxPaletteColors)
                : summary.count == 0 ? Localization.Text("Видимых цветов нет") : Localization.Format("Цветов: {0:N0}. Нажмите на цвет, чтобы заменить его.", summary.count);
            infoManualReplaceButton.Visibility = summary.count > ImageColorTools.MaxPaletteColors
                ? Visibility.Visible : Visibility.Collapsed;
            foreach (DrawingColor color in summary.palette)
                _infoPalette.Add(new PaletteColorEntry(color));
        }
        catch (Exception ex)
        {
            if (version != _infoRefreshVersion) return;
            _infoSnapshotReady = false;
            _infoSourceShown = _infoResultShown = _infoBasisShown = null;
            infoPaletteStatus.Text = Localization.Format("Не удалось прочитать цвета: {0}", ex.Message);
        }
    }

    private sealed record InfoSummary(int Width, int Height, int Bits, string Layout, float DpiX, float DpiY,
        int Colors, long Transparent, long Partial, long Opaque, bool Grayscale, IReadOnlyList<DrawingColor> Palette);

    private static InfoSummary ReadInfoSummary(Bitmap image)
    {
        var colors = new HashSet<int>();
        var palette = new List<DrawingColor>();
        long transparent = 0, partial = 0, opaque = 0;
        bool grayscale = true;
        using var pixels = image.Clone(new System.Drawing.Rectangle(0, 0, image.Width, image.Height), PixelFormat.Format32bppArgb);
        var bits = pixels.LockBits(new System.Drawing.Rectangle(0, 0, pixels.Width, pixels.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new int[pixels.Width];
            for (int q = 0; q < pixels.Height; q++)
            {
                Marshal.Copy(IntPtr.Add(bits.Scan0, q * bits.Stride), row, 0, row.Length);
                for (int e = 0; e < row.Length; e++)
                {
                    uint pixel = (uint)row[e];
                    uint alpha = pixel >> 24;
                    if (alpha == 0) { transparent++; continue; }
                    if (alpha == 255) opaque++; else partial++;
                    int rgb = (int)(pixel & 0xFFFFFF);
                    if (colors.Add(rgb) && colors.Count <= ImageColorTools.MaxPaletteColors)
                        palette.Add(DrawingColor.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255));
                    if (((pixel >> 16) & 255) != ((pixel >> 8) & 255) || ((pixel >> 8) & 255) != (pixel & 255)) grayscale = false;
                }
            }
        }
        finally { pixels.UnlockBits(bits); }
        return new(image.Width, image.Height, System.Drawing.Image.GetPixelFormatSize(image.PixelFormat), image.PixelFormat.ToString(),
            image.HorizontalResolution, image.VerticalResolution, colors.Count, transparent, partial, opaque, grayscale,
            colors.Count <= ImageColorTools.MaxPaletteColors ? palette : []);
    }

    private static string FormatInfoDetails(InfoSummary info, long? fileSize)
    {
        int width = info.Width, height = info.Height;
        while (height != 0) { int remainder = width % height; width = height; height = remainder; }
        long pixels = (long)info.Width * info.Height;
        string type = info.Colors == 0 ? Localization.Text("Нет видимых пикселей") : info.Grayscale ? Localization.Text("Серые тона") : "RGB";
        string dpi = float.IsFinite(info.DpiX) && float.IsFinite(info.DpiY) && info.DpiX > 0 && info.DpiY > 0
            ? Localization.Format("DPI: {0:0.#} × {1:0.#}", info.DpiX, info.DpiY) : Localization.Text("DPI: не указано");
        var lines = new List<string>
        {
            Localization.Format("Пикселей: {0:N0} · пропорции {1} : {2}", pixels, info.Width / width, info.Height / width),
            Localization.Format("Цвета пикселей: {0}", type),
            Localization.Format("Формат буфера: {0} · {1} бит/пиксель", info.Layout, info.Bits),
            Localization.Format("Прозрачные: {0:N0} ({1:P1})", info.Transparent, info.Transparent / (double)pixels),
            Localization.Format("Полупрозрачные: {0:N0} ({1:P1})", info.Partial, info.Partial / (double)pixels),
            Localization.Format("Непрозрачные: {0:N0} ({1:P1})", info.Opaque, info.Opaque / (double)pixels),
            dpi
        };
        if (fileSize is { } size) lines.Add(Localization.Format("PNG в библиотеке: {0:N0} байт", size));
        return string.Join("\n", lines);
    }

    private static long? InfoFileSize(string? path)
    {
        if (path is null) return null;
        try { return new FileInfo(path).Length; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private void InfoColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PaletteColorEntry color })
            OpenColorReplace(color.Color, false);
    }

    private void OpenColorReplace(DrawingColor oldColor, bool allowOldColorEdit)
    {
        if (_processing || _sourceImage is null) return;
        if (!InfoToolVisible) OpenEditorTool("info", Localization.Text("Цвета и сведения"), 760, infoToolGroup);
        RefreshInfo();
        if (_infoBasisShown is null || !InfoToolVisible) return;
        Window owner = _toolWindows.TryGetValue("info", out var tool) ? tool : this;
        _replaceWindow?.Close();
        Bitmap basis = _infoBasisShown;
        var dialog = new ColorReplaceWindow(oldColor, _infoPalette.ToArray(), allowOldColorEdit)
        {
            Owner = owner
        };
        _replaceWindow = dialog;
        dialog.Closed += (_, _) =>
        {
            if (ReferenceEquals(_replaceWindow, dialog)) _replaceWindow = null;
        };
        dialog.ReplaceRequested += (previous, replacement) =>
        {
            if (ReferenceEquals(basis, _infoBasisShown)) ReplaceInfoColor(previous, replacement);
        };
        dialog.Show();
    }

    private async void ReplaceInfoColor(DrawingColor oldColor, DrawingColor newColor)
    {
        if (_processing || _activeSource is null || oldColor.ToArgb() == newColor.ToArgb()) return;

        bool useResult = ProcessingBasis == 1 && _downscaledResult is not null;
        Bitmap? basis = useResult ? _downscaledResult : _sourceImage;
        if (basis is null) return;
        var document = _activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        var settings = CaptureSettings();
        using var input = (Bitmap)basis.Clone();
        SetProcessingState(true);
        statusLabel.Text = Localization.Text("Замена цвета…");
        try
        {
            var generation = await Task.Run(() =>
            {
                using var output = ImageColorTools.ReplaceColor(input, oldColor, newColor);
                return _library.SaveGeneration(document, output, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = Localization.Text("Замена цвета"),
                    ParentGenerationId = parentId,
                    PreservesTransparency = true,
                    Caption = Localization.Format("ЗАМЕНА ЦВЕТА · {0} × {1}", output.Width, output.Height),
                    Log = Localization.Format("#{0:X2}{1:X2}{2:X2} → #{3:X2}{4:X2}{5:X2}. Размер и прозрачность сохранены.", oldColor.R, oldColor.G, oldColor.B, newColor.R, newColor.G, newColor.B)
                });
            });
            document.Generations.Add(generation);
            SelectGeneration(generation);
            statusLabel.Text = Localization.Text("Цвет заменён. Результат сохранён в истории.");
        }
        catch (Exception ex)
        {
            statusLabel.Text = Localization.Format("Не удалось заменить цвет: {0}", ex.Message);
        }
        finally { SetProcessingState(false); }
    }
}
