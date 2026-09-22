using System.Collections.ObjectModel;
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

    private void InitializeInfo()
    {
        infoPaletteList.ItemsSource = _infoPalette;
        infoManualReplaceButton.Click += (_, _) => OpenColorReplace(DrawingColor.Black, true);
    }

    private void ResetInfoSnapshot()
    {
        _infoRefreshVersion++;
        _replaceWindow?.Close();
        _infoSourceShown = _infoResultShown = _infoBasisShown = null;
        _infoPalette.Clear();
    }

    private async void RefreshInfo()
    {
        if (_processing || !_toolWindows.ContainsKey("info")) return;
        Bitmap? source = _sourceImage;
        Bitmap? result = _downscaledResult;
        Bitmap? basis = operationSourceInput.SelectedIndex == 1 && result is not null ? result : source;
        if (ReferenceEquals(source, _infoSourceShown) &&
            ReferenceEquals(result, _infoResultShown) &&
            ReferenceEquals(basis, _infoBasisShown)) return;

        if (!ReferenceEquals(basis, _infoBasisShown)) _replaceWindow?.Close();
        _infoSourceShown = source;
        _infoResultShown = result;
        _infoBasisShown = basis;
        long version = ++_infoRefreshVersion;
        infoSourceSummary.Text = source is null ? "Нет изображения" : $"{source.Width} × {source.Height} px · подсчёт цветов…";
        infoResultSummary.Text = result is null ? "Пока нет результата" : $"{result.Width} × {result.Height} px · подсчёт цветов…";
        infoBasisLabel.Text = ReferenceEquals(basis, result) && result is not null
            ? "Цвета выбранного результата" : "Цвета исходника";
        infoPaletteStatus.Text = basis is null ? "Откройте изображение" : "Подсчёт видимых цветов…";
        _infoPalette.Clear();
        infoManualReplaceButton.Visibility = Visibility.Collapsed;
        if (basis is null) return;

        using var sourceCopy = source is null ? null : (Bitmap)source.Clone();
        using var resultCopy = result is null ? null : (Bitmap)result.Clone();
        try
        {
            var summary = await Task.Run(() =>
            {
                int sourceColors = sourceCopy is null ? 0 : ImageColorTools.CountColors(sourceCopy);
                int resultColors = resultCopy is null ? 0 : ImageColorTools.CountColors(resultCopy);
                Bitmap selected = ReferenceEquals(basis, result) ? resultCopy! : sourceCopy!;
                int count = ReferenceEquals(basis, result) ? resultColors : sourceColors;
                IReadOnlyList<DrawingColor> palette = count <= ImageColorTools.MaxPaletteColors
                    ? ImageColorTools.GetPalette(selected) : [];
                return (sourceColors, resultColors, count, palette);
            });
            if (version != _infoRefreshVersion || !_toolWindows.ContainsKey("info")) return;

            if (source is not null)
                infoSourceSummary.Text = $"{source.Width} × {source.Height} px · {summary.sourceColors:N0} видимых цветов";
            if (result is not null)
                infoResultSummary.Text = $"{result.Width} × {result.Height} px · {summary.resultColors:N0} видимых цветов";
            infoPaletteStatus.Text = summary.count > ImageColorTools.MaxPaletteColors
                ? $"Цветов: {summary.count:N0}. Список доступен до {ImageColorTools.MaxPaletteColors} цветов."
                : summary.count == 0 ? "Видимых цветов нет" : $"Цветов: {summary.count:N0}. Нажмите на цвет, чтобы заменить его.";
            infoManualReplaceButton.Visibility = summary.count > ImageColorTools.MaxPaletteColors
                ? Visibility.Visible : Visibility.Collapsed;
            foreach (DrawingColor color in summary.palette)
                _infoPalette.Add(new PaletteColorEntry(color));
        }
        catch (Exception ex)
        {
            if (version != _infoRefreshVersion) return;
            _infoSourceShown = _infoResultShown = _infoBasisShown = null;
            infoPaletteStatus.Text = $"Не удалось прочитать цвета: {ex.Message}";
        }
    }

    private void InfoColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PaletteColorEntry color })
            OpenColorReplace(color.Color, false);
    }

    private void OpenColorReplace(DrawingColor oldColor, bool allowOldColorEdit)
    {
        if (_processing || _infoBasisShown is null || !_toolWindows.TryGetValue("info", out var owner)) return;
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

        bool useResult = operationSourceInput.SelectedIndex == 1 && _downscaledResult is not null;
        Bitmap? basis = useResult ? _downscaledResult : _sourceImage;
        if (basis is null) return;
        var document = _activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        var settings = CaptureSettings();
        using var input = (Bitmap)basis.Clone();
        SetProcessingState(true);
        statusLabel.Text = "Замена цвета…";
        try
        {
            var generation = await Task.Run(() =>
            {
                using var output = ImageColorTools.ReplaceColor(input, oldColor, newColor);
                return _library.SaveGeneration(document, output, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = "Замена цвета",
                    ParentGenerationId = parentId,
                    PreservesTransparency = true,
                    Caption = $"ЗАМЕНА ЦВЕТА · {output.Width} × {output.Height}",
                    Log = $"#{oldColor.R:X2}{oldColor.G:X2}{oldColor.B:X2} → #{newColor.R:X2}{newColor.G:X2}{newColor.B:X2}. Размер и прозрачность сохранены."
                });
            });
            document.Generations.Add(generation);
            SelectGeneration(generation);
            statusLabel.Text = "Цвет заменён. Результат сохранён в истории.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Не удалось заменить цвет: {ex.Message}";
        }
        finally { SetProcessingState(false); }
    }
}
