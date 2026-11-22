using System.Collections.ObjectModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tessera.Core.Imaging;
using Bitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;

namespace Tessera.Desktop;

internal sealed class EditorInfoTool
{
    private readonly ImageEditorPage _editor;
    private MainWindow Host => _editor.Host;

    internal EditorInfoTool(ImageEditorPage editor)
    {
        _editor = editor;
    }

    private readonly ObservableCollection<PaletteColorEntry> _infoPalette = [];

    private ColorReplaceWindow? _replaceWindow;

    private Bitmap? _infoSourceShown;

    private Bitmap? _infoResultShown;

    private Bitmap? _infoBasisShown;

    private long _infoRefreshVersion;

    private Button _infoEyedropperButton = null!;

    private bool _pickingInfoColor;

    private int? _highlightInfoColor;

    private int _infoColorCount;

    private bool _infoSnapshotReady;

    private string? _infoLanguageShown;

    private bool IsInfoToolOpen => _editor.Dock.FloatingWindow("info") is not null || _editor.Dock.IsShownInDock("info");

    private PixelPreview[] ColorSelectionPreviews => [_editor.sourcePreview, _editor.resultPreview, _editor.Preview.ComparisonSurface];

    internal void InitializeInfo()
    {
        _editor.infoToolGroup.Header = Localization.Text("Info");
        System.Windows.Automation.AutomationProperties.SetName(_editor.infoColorTreemap, Localization.Text("ImageColorMap"));
        _editor.infoColorTreemap.ColorSelected += color => OpenColorReplace(color, false);
        _editor.infoManualReplaceButton.Click += (_, _) => OpenColorReplace(DrawingColor.Black, true);
        _infoEyedropperButton = new Button
        {
            Content = Localization.Text("EyedropperChooseAColorFromThe"),
            ToolTip = Localization.Text("ChooseAPixelFromTheProcessing"),
            Margin = new Thickness(0, 0, 0, 10),
            IsEnabled = false
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_infoEyedropperButton, "infoEyedropperButton");
        _infoEyedropperButton.Click += (_, _) => BeginInfoColorPicking();
        if (_editor.infoToolGroup.Content is StackPanel content)
            content.Children.Insert(content.Children.IndexOf(_editor.infoPaletteStatus) + 1, _infoEyedropperButton);
        foreach (PixelPreview preview in ColorSelectionPreviews)
        {
            preview.ColorPicked += InfoColorPicked;
            preview.ImageChanged += (_, _) =>
            {
                if ((ReferenceEquals(preview, _editor.sourcePreview) && !ReferenceEquals(preview.Image, _infoSourceShown)) ||
                    (ReferenceEquals(preview, _editor.resultPreview) && !ReferenceEquals(preview.Image, _infoResultShown)))
                {
                    _replaceWindow?.Close();
                    ClearInfoColorSelection();
                }
                UpdateInfoColorSelection();
            };
        }
        _editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || !_pickingInfoColor) return;
            StopInfoColorPicking();
            e.Handled = true;
        };
        _editor.infoToolGroup.Unloaded += (_, _) => _editor.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsInfoToolOpen) ResetInfoSnapshot();
        }));
    }

    internal void ResetInfoSnapshot()
    {
        _infoRefreshVersion++;
        _infoSnapshotReady = false;
        _infoLanguageShown = null;
        _replaceWindow?.Close();
        ClearInfoColorSelection();
        _infoSourceShown = _infoResultShown = _infoBasisShown = null;
        _infoPalette.Clear();
        _infoColorCount = 0;
        _editor.infoColorTreemap.SetAreas([]);
        _infoEyedropperButton.IsEnabled = false;
    }

    internal async void RefreshInfo()
    {
        if (_editor._processing || !IsInfoToolOpen) return;
        Bitmap? source = _editor._sourceImage;
        Bitmap? result = _editor._downscaledResult;
        Bitmap? basis = _editor.ProcessingBasis == 1 && result is not null ? result : source;
        if (_infoSnapshotReady && ReferenceEquals(source, _infoSourceShown) &&
            ReferenceEquals(result, _infoResultShown) &&
            ReferenceEquals(basis, _infoBasisShown) && _infoLanguageShown == Localization.Language) return;

        _replaceWindow?.Close();
        ClearInfoColorSelection();
        _infoSnapshotReady = true;
        _infoLanguageShown = Localization.Language;
        _infoSourceShown = source;
        _infoResultShown = result;
        _infoBasisShown = basis;
        long version = ++_infoRefreshVersion;
        _editor.infoSourceSummary.Text = source is null ? Localization.Text("NoImage") : Localization.Format("PxCountingColorsAction", source.Width, source.Height);
        _editor.infoResultSummary.Text = result is null ? Localization.Text("NoResultYet") : Localization.Format("PxCountingColorsAction", result.Width, result.Height);
        _editor.infoBasisLabel.Text = ReferenceEquals(basis, result) && result is not null
            ? Localization.Text("SelectedResultColors") : Localization.Text("SourceColors");
        _editor.infoPaletteStatus.Text = basis is null ? Localization.Text("OpenAnImage") : Localization.Text("CountingVisibleColorsAction");
        _infoPalette.Clear();
        _infoColorCount = 0;
        _editor.infoColorTreemap.SetAreas([]);
        _editor.infoSourceDetails.Text = _editor.infoResultDetails.Text = "";
        _editor.infoManualReplaceButton.Visibility = Visibility.Collapsed;
        _infoEyedropperButton.IsEnabled = false;
        if (basis is null) return;

        using var sourceCopy = source is null ? null : (Bitmap)source.Clone();
        using var resultCopy = result is null ? null : (Bitmap)result.Clone();
        try
        {
            var sourceFileSize = InfoFileSize(_editor._activeSource is null ? null : Host._library.SourcePath(_editor._activeSource));
            var resultFileSize = InfoFileSize(_editor._activeSource?.SelectedGeneration is { } generation ? Host._library.ResultPath(_editor._activeSource, generation) : null);
            var summary = await Task.Run(() =>
            {
                var sourceInfo = sourceCopy is null ? null : ReadInfoSummary(sourceCopy);
                var resultInfo = resultCopy is null ? null : ReadInfoSummary(resultCopy);
                var selected = ReferenceEquals(basis, result) ? resultInfo! : sourceInfo!;
                return (sourceInfo, resultInfo, count: selected.Colors, palette: selected.Palette);
            });
            if (version != _infoRefreshVersion || !IsInfoToolOpen) return;

            if (summary.sourceInfo is { } input)
            {
                _editor.infoSourceSummary.Text = Localization.Format("PxVisibleColorsFormat", input.Width, input.Height, input.Colors);
                _editor.infoSourceDetails.Text = FormatInfoDetails(input, sourceFileSize);
            }
            if (summary.resultInfo is { } output)
            {
                _editor.infoResultSummary.Text = Localization.Format("PxVisibleColorsFormat", output.Width, output.Height, output.Colors);
                _editor.infoResultDetails.Text = FormatInfoDetails(output, resultFileSize);
            }
            _editor.infoPaletteStatus.Text = summary.count > ImageColorTools.MaxPaletteColors
                ? Localization.Format("TreemapGroupedColorCount", summary.count, ImageColorTools.MaxPaletteColors)
                : summary.count == 0 ? Localization.Text("NoVisibleColors") : Localization.Format("ImageColorCountWithReplaceHint", summary.count);
            _editor.infoManualReplaceButton.Visibility = summary.count > ImageColorTools.MaxPaletteColors
                ? Visibility.Visible : Visibility.Collapsed;
            _infoColorCount = summary.count;
            _editor.infoColorTreemap.SetAreas(summary.palette);
            foreach (var area in summary.palette)
                if (area.Color is { } color) _infoPalette.Add(new PaletteColorEntry(color));
            _infoEyedropperButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Host._mainProgress?.Dispose();
            if (version != _infoRefreshVersion) return;
            _infoSnapshotReady = false;
            _infoSourceShown = _infoResultShown = _infoBasisShown = null;
            _editor.infoPaletteStatus.Text = Localization.Format("CouldNotReadColorsFormat", ex.Message);
        }
    }

    private sealed record InfoSummary(int Width, int Height, int Bits, string Layout, float DpiX, float DpiY,
        int Colors, long Transparent, long Partial, long Opaque, bool Grayscale, IReadOnlyList<ColorArea> Palette);

    private static InfoSummary ReadInfoSummary(Bitmap image)
    {
        var colors = new Dictionary<int, long>();
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
                    colors.TryGetValue(rgb, out long count);
                    colors[rgb] = count + 1;
                    if (((pixel >> 16) & 255) != ((pixel >> 8) & 255) || ((pixel >> 8) & 255) != (pixel & 255)) grayscale = false;
                }
            }
        }
        finally { pixels.UnlockBits(bits); }
        var palette = colors.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key)
            .Take(ImageColorTools.MaxPaletteColors)
            .Select(pair => new ColorArea(DrawingColor.FromArgb(255, (pair.Key >> 16) & 255, (pair.Key >> 8) & 255, pair.Key & 255), pair.Value))
            .ToList();
        long grouped = opaque + partial - palette.Sum(area => area.Pixels);
        if (grouped > 0) palette.Add(new ColorArea(null, grouped));
        return new(image.Width, image.Height, System.Drawing.Image.GetPixelFormatSize(image.PixelFormat), image.PixelFormat.ToString(),
            image.HorizontalResolution, image.VerticalResolution, colors.Count, transparent, partial, opaque, grayscale, palette);
    }

    private static string FormatInfoDetails(InfoSummary info, long? fileSize)
    {
        int width = info.Width, height = info.Height;
        while (height != 0) { int remainder = width % height; width = height; height = remainder; }
        long pixels = (long)info.Width * info.Height;
        string type = info.Colors == 0 ? Localization.Text("NoVisiblePixels") : info.Grayscale ? Localization.Text("Grayscale") : "RGB";
        string dpi = float.IsFinite(info.DpiX) && float.IsFinite(info.DpiY) && info.DpiX > 0 && info.DpiY > 0
            ? Localization.Format("DPIFormat", info.DpiX, info.DpiY) : Localization.Text("DPIUnspecified");
        var lines = new List<string>
        {
            Localization.Format("PixelsAspectRatioFormat", pixels, info.Width / width, info.Height / width),
            Localization.Format("PixelColorsFormat", type),
            Localization.Format("BufferFormatBitsPixelFormat", info.Layout, info.Bits),
            Localization.Format("TransparentFormat", info.Transparent, info.Transparent / (double)pixels),
            Localization.Format("TranslucentFormat", info.Partial, info.Partial / (double)pixels),
            Localization.Format("OpaqueFormat", info.Opaque, info.Opaque / (double)pixels),
            dpi
        };
        if (fileSize is { } size) lines.Add(Localization.Format("LibraryPNGBytesFormat", size));
        return string.Join("\n", lines);
    }

    private static long? InfoFileSize(string? path)
    {
        if (path is null) return null;
        try { return new FileInfo(path).Length; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    internal void OpenColorReplace(DrawingColor oldColor, bool allowOldColorEdit)
    {
        if (_editor._processing || _editor._sourceImage is null) return;
        if (!IsInfoToolOpen) _editor.Dock.OpenEditorTool("info", Localization.Text("Info"), 760, _editor.infoToolGroup);
        RefreshInfo();
        if (_infoBasisShown is null || !IsInfoToolOpen) return;
        _replaceWindow?.Close();
        Bitmap basis = _infoBasisShown;
        PaletteColorEntry[] palette = allowOldColorEdit && _infoColorCount > ImageColorTools.MaxPaletteColors
            ? [] : _infoPalette.ToArray();
        var dialog = new ColorReplaceWindow(oldColor, palette, allowOldColorEdit)
        {
            Owner = _editor.Dock.FloatingWindow("info") ?? Host
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
        if (_editor._processing || _infoBasisShown is null || !IsInfoToolOpen) return;
        if (_pickingInfoColor) { StopInfoColorPicking(); return; }
        _pickingInfoColor = true;
        _infoEyedropperButton.Content = Localization.Text("CancelEyedropperEsc");
        UpdateInfoColorSelection();
    }

    private void StopInfoColorPicking()
    {
        _pickingInfoColor = false;
        _infoEyedropperButton.Content = Localization.Text("EyedropperChooseAColorFromThe");
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
        if (!_pickingInfoColor || _editor._processing || sender is not PixelPreview preview ||
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
        if (_editor._processing || _editor._activeSource is null || oldColor.ToArgb() == newColor.ToArgb()) return;

        bool useResult = _editor.ProcessingBasis == 1 && _editor._downscaledResult is not null;
        Bitmap? basis = useResult ? _editor._downscaledResult : _editor._sourceImage;
        if (basis is null) return;
        var document = _editor._activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        var settings = _editor.Tools.CaptureSettings();
        using var input = (Bitmap)basis.Clone();
        _editor.SetProcessingState(true);
        Host.statusLabel.Text = Localization.Text("ReplacingColorAction");
        try
        {
            var generation = await Task.Run(() =>
            {
                using var output = ImageColorTools.ReplaceColor(input, oldColor, newColor, Host.CurrentProcessingProgress);
                return Host._library.SaveGeneration(document, output, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = Localization.Text("ColorReplacement"),
                    OperationId = ProcessingOperations.ReplaceColor,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = ProcessingOperations.ReplaceColor, ColorReplacement = new ColorReplacementSettings { FromArgb = oldColor.ToArgb(), ToArgb = newColor.ToArgb() } }),
                    ParentGenerationId = parentId,
                    PreservesTransparency = true,
                    Caption = Localization.Format("ColorReplacementFormat", output.Width, output.Height),
                    Log = Localization.Format("SizeAndTransparencyPreservedFormat", oldColor.R, oldColor.G, oldColor.B, newColor.R, newColor.G, newColor.B)
                });
            });
            if (!_editor.History.CommitGeneration(document, generation)) return;
            Host.statusLabel.Text = Localization.Text("ColorReplaced");
        }
        catch (Exception ex)
        {
            Host._mainProgress?.Dispose();
            Host.statusLabel.Text = Localization.Format("CouldNotReplaceColorFormat", ex.Message);
        }
        finally { _editor.SetProcessingState(false); }
    }
}
