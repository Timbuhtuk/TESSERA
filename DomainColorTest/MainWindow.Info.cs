using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private Button _infoEyedropperButton = null!;
    private bool _pickingInfoColor;
    private int? _highlightInfoColor;

    private bool IsInfoToolOpen => _toolWindows.ContainsKey("info") || _workspaceGroups.Contains(infoToolGroup);
    private PixelPreview[] ColorSelectionPreviews => [sourcePreview, resultPreview, _comparisonPreview];

    private void InitializeInfo()
    {
        infoPaletteList.ItemsSource = _infoPalette;
        infoManualReplaceButton.Click += (_, _) => OpenColorReplace(DrawingColor.Black, true);
        _infoEyedropperButton = new Button
        {
            Content = Localization.Text("Пипетка: выбрать цвет на изображении"),
            ToolTip = Localization.Text("Выберите пиксель на изображении основы. Его цвет будет выбран для замены. Esc отменяет пипетку."),
            Margin = new Thickness(0, 0, 0, 10),
            IsEnabled = false
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_infoEyedropperButton, "infoEyedropperButton");
        _infoEyedropperButton.Click += (_, _) => BeginInfoColorPicking();
        if (infoToolGroup.Content is StackPanel content)
            content.Children.Insert(content.Children.IndexOf(infoPaletteStatus) + 1, _infoEyedropperButton);
        foreach (PixelPreview preview in ColorSelectionPreviews)
        {
            preview.ColorPicked += InfoColorPicked;
            preview.ImageChanged += (_, _) =>
            {
                if ((ReferenceEquals(preview, sourcePreview) && !ReferenceEquals(preview.Image, _infoSourceShown)) ||
                    (ReferenceEquals(preview, resultPreview) && !ReferenceEquals(preview.Image, _infoResultShown)))
                {
                    _replaceWindow?.Close();
                    ClearInfoColorSelection();
                }
                UpdateInfoColorSelection();
            };
        }
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || !_pickingInfoColor) return;
            StopInfoColorPicking();
            e.Handled = true;
        };
        infoToolGroup.Unloaded += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsInfoToolOpen) ResetInfoSnapshot();
        }));
    }

    private void ResetInfoSnapshot()
    {
        _infoRefreshVersion++;
        _replaceWindow?.Close();
        ClearInfoColorSelection();
        _infoSourceShown = _infoResultShown = _infoBasisShown = null;
        _infoPalette.Clear();
        _infoEyedropperButton.IsEnabled = false;
    }

    private async void RefreshInfo()
    {
        if (_processing || !IsInfoToolOpen) return;
        Bitmap? source = _sourceImage;
        Bitmap? result = _downscaledResult;
        Bitmap? basis = ProcessingBasis == 1 && result is not null ? result : source;
        if (ReferenceEquals(source, _infoSourceShown) &&
            ReferenceEquals(result, _infoResultShown) &&
            ReferenceEquals(basis, _infoBasisShown)) return;

        _replaceWindow?.Close();
        ClearInfoColorSelection();
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
        infoManualReplaceButton.Visibility = Visibility.Collapsed;
        _infoEyedropperButton.IsEnabled = false;
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
            if (version != _infoRefreshVersion || !IsInfoToolOpen) return;

            if (source is not null)
                infoSourceSummary.Text = Localization.Format("{0} × {1} px · {2:N0} видимых цветов", source.Width, source.Height, summary.sourceColors);
            if (result is not null)
                infoResultSummary.Text = Localization.Format("{0} × {1} px · {2:N0} видимых цветов", result.Width, result.Height, summary.resultColors);
            infoPaletteStatus.Text = summary.count > ImageColorTools.MaxPaletteColors
                ? Localization.Format("Цветов: {0:N0}. Список доступен до {1} цветов.", summary.count, ImageColorTools.MaxPaletteColors)
                : summary.count == 0 ? Localization.Text("Видимых цветов нет") : Localization.Format("Цветов: {0:N0}. Нажмите на цвет, чтобы заменить его.", summary.count);
            infoManualReplaceButton.Visibility = summary.count > ImageColorTools.MaxPaletteColors
                ? Visibility.Visible : Visibility.Collapsed;
            foreach (DrawingColor color in summary.palette)
                _infoPalette.Add(new PaletteColorEntry(color));
            _infoEyedropperButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            if (version != _infoRefreshVersion) return;
            _infoSourceShown = _infoResultShown = _infoBasisShown = null;
            infoPaletteStatus.Text = Localization.Format("Не удалось прочитать цвета: {0}", ex.Message);
        }
    }

    private void InfoColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PaletteColorEntry color })
            OpenColorReplace(color.Color, false);
    }

    private void OpenColorReplace(DrawingColor oldColor, bool allowOldColorEdit)
    {
        if (_processing || _infoBasisShown is null || !IsInfoToolOpen) return;
        _replaceWindow?.Close();
        Bitmap basis = _infoBasisShown;
        var dialog = new ColorReplaceWindow(oldColor, _infoPalette.ToArray(), allowOldColorEdit)
        {
            Owner = _toolWindows.TryGetValue("info", out var owner) ? owner : this
        };
        _replaceWindow = dialog;
        dialog.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_replaceWindow, dialog)) return;
            _replaceWindow = null;
            ClearInfoColorSelection();
        };
        dialog.SourceColorChanged += color =>
        {
            _highlightInfoColor = color.HasValue && _infoPalette.Count > 0 ? color.Value.ToArgb() & 0xFFFFFF : null;
            UpdateInfoColorSelection();
        };
        dialog.PickSourceRequested += BeginInfoColorPicking;
        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || !_pickingInfoColor) return;
            StopInfoColorPicking();
            e.Handled = true;
        };
        dialog.ReplaceRequested += (previous, replacement) =>
        {
            if (ReferenceEquals(basis, _infoBasisShown)) ReplaceInfoColor(previous, replacement);
        };
        _highlightInfoColor = _infoPalette.Count > 0 ? oldColor.ToArgb() & 0xFFFFFF : null;
        UpdateInfoColorSelection();
        dialog.Show();
    }

    private void BeginInfoColorPicking()
    {
        if (_processing || _infoBasisShown is null || !IsInfoToolOpen) return;
        if (_pickingInfoColor) { StopInfoColorPicking(); return; }
        _pickingInfoColor = true;
        _infoEyedropperButton.Content = Localization.Text("Отменить пипетку (Esc)");
        UpdateInfoColorSelection();
    }

    private void StopInfoColorPicking()
    {
        _pickingInfoColor = false;
        _infoEyedropperButton.Content = Localization.Text("Пипетка: выбрать цвет на изображении");
        UpdateInfoColorSelection();
    }

    private void ClearInfoColorSelection()
    {
        _highlightInfoColor = null;
        StopInfoColorPicking();
    }

    private void UpdateInfoColorSelection()
    {
        foreach (PixelPreview preview in ColorSelectionPreviews)
        {
            bool basis = _infoBasisShown is not null && ReferenceEquals(preview.Image, _infoBasisShown);
            preview.IsColorPicking = basis && _pickingInfoColor;
            preview.HighlightRgb = basis ? _highlightInfoColor : null;
        }
    }

    private void InfoColorPicked(object? sender, PixelColorPickedEventArgs e)
    {
        if (!_pickingInfoColor || _processing || sender is not PixelPreview preview ||
            !ReferenceEquals(preview.Image, _infoBasisShown)) return;
        StopInfoColorPicking();
        if (_replaceWindow is { } dialog)
        {
            dialog.SetSourceColor(e.Color);
            dialog.Activate();
        }
        else OpenColorReplace(e.Color, true);
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
