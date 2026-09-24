using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using Bitmap = System.Drawing.Bitmap;
using PixelArtDownscale;
using PixelArtAlignment;

namespace DomainColorTest;

public partial class MainWindow : Window
{
    private readonly PixelArtDownscaler _downscaler = new();
    private string? _selectedFilePath;
    private Bitmap? _sourceImage;
    private Bitmap? _downscaledResult;
    private bool _resultPreservesTransparency;
    private bool _resultIsAlignment;
    private bool _applyingProfile;
    private bool _capturingReadyMode;
    private bool _updatingSize;
    private bool _processing;
    private bool _resultStale;

    private readonly OpenFileDialog openDialog = new() { Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.gif", Title = "Добавить изображения", Multiselect = true };
    private readonly SaveFileDialog saveDialog = new() { DefaultExt = ".png", AddExtension = true, OverwritePrompt = true, Title = "Сохранить результат" };

    public MainWindow() : this(LibraryLocation.PrepareDefault()) { _autoCheckUpdates = true; }

    public MainWindow(string libraryDirectory)
    {
        _library = new ImageLibrary(libraryDirectory);
        InitializeComponent();
        InitializeWindowFrame();
        threadsInput.Maximum = threadsInput.Value = Math.Max(1, Environment.ProcessorCount);
        foreach (var mode in new[] { backgroundMode, sceneMode, detailsMode })
            mode.Checked += (_, _) => UpdateModeDescription();
        foreach (var box in new[] { paletteInput, quantizationInput, cropHorizontalInput, cropVerticalInput })
            box.SelectionChanged += SettingsChanged;
        foreach (var input in new[] { paletteStepInput, alphaInput, quantizationColorsInput, localColorPassesInput })
            input.ValueChanged += SettingsChanged;
        foreach (var check in new ToggleButton[] { spriteInput, ditheringInput, colorWeightsInput, automaticInput, manualInput,
            quantizationColorModeInput, paletteColorModeInput })
        {
            check.Checked += SettingsChanged;
            check.Unchecked += SettingsChanged;
        }
        foreach (var slider in new[] { brightnessInput, contrastInput, saturationInput, edgeInput,
            localBrightnessInput, localContrastInput, localSaturationInput, localEdgeInput })
            slider.ValueChanged += (_, _) => SettingsChanged(null, EventArgs.Empty);
        widthInput.ValueChanged += (_, _) => SizeChangedByUser(widthChanged: true);
        heightInput.ValueChanged += (_, _) => SizeChangedByUser(widthChanged: false);
        aspectLock.Checked += (_, _) => SizeChangedByUser(widthChanged: true);
        zoomInput.SelectionChanged += (_, _) => UpdatePreviewSettings();
        backgroundInput.SelectionChanged += (_, _) => UpdatePreviewSettings();
        detectGridInput.Checked += (_, _) => { gridCellInput.IsEnabled = false; AlignmentSettingsChanged(); };
        detectGridInput.Unchecked += (_, _) => { gridCellInput.IsEnabled = true; AlignmentSettingsChanged(); };
        gridCellInput.ValueChanged += (_, _) => AlignmentSettingsChanged();
        openButton.Click += (sender, e) => { fileMenuPopup.IsOpen = false; OpenImage(sender, e); };
        processButton.Click += ProcessImage;
        scaleOnlyButton.Click += ScaleOnlyImage;
        colorOnlyButton.Click += ApplyColorsOnly;
        neighborColorsButton.Click += ApplyNeighborColorsOnly;
        operationSourceInput.SelectionChanged += (_, _) =>
        {
            if (aspectLock.IsChecked == true) SizeChangedByUser(widthChanged: true);
            else UpdateSizeHint();
            SetProcessingState(_processing);
        };
        saveButton.Click += (sender, e) => { fileMenuPopup.IsOpen = false; SaveImage(sender, e); };
        saveAllButton.Click += SaveAllImages;
        alignButton.Click += AlignImage;
        Closing += (_, e) => { if (_processing || iconWorkspace.IsBusy || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy) { e.Cancel = true; statusLabel.Text = "Дождитесь завершения обработки."; } };
        Closed += (_, _) =>
        {
            CloseEditorTools();
            _restoringHistory = true;
            sourcePreview.Image = resultPreview.Image = null;
            _sourceImage?.Dispose(); _sourceImage = null;
            _downscaledResult?.Dispose(); _downscaledResult = null;
        };
        sceneMode.IsChecked = true;
        InitializeLibrary();
        InitializeImageActions();
        InitializeHome();
        InitializeIcons();
        InitializeAseprite();
        InitializeBackground();
        InitializeEditorTools();
        InitializeInfo();
        InitializeFilters();
        InitializeUpdates();
        UpdatePreviewSettings();
        UpdateDependentControls();
        if (LibraryLocation.MigrationWarning is { } warning)
            statusLabel.Text = $"Не удалось полностью перенести старую историю: {warning}";
    }

    private string CurrentModeName => detailsMode.IsChecked == true ? "Детали" : backgroundMode.IsChecked == true ? "Фон" : "Сцена";
    private static void SetVisible(UIElement control, bool visible) => control.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    private void UpdateModeDescription()
    {
        modeDescription.Text = detailsMode.IsChecked == true
            ? "Пиксель-арт и спрайты: приоритет контуров и прозрачности. При уменьшении часть деталей может исчезнуть."
            : backgroundMode.IsChecked == true
                ? "Крупные фоновые изображения: цельные цветовые области и общая композиция, без акцента на мелочах."
                : "Сцены средней детализации: сохраняем значимые формы, свет и цветовые переходы.";
        profileState.Text = "Параметры режима применятся при обработке";
    }

    private void ApplyProfile()
    {
        _applyingProfile = true;
        try
        {
            bool details = detailsMode.IsChecked == true;
            quantizationInput.SelectedIndex = details ? 0 : 1;
            quantizationColorsInput.Value = 64;
            colorWeightsInput.IsChecked = false;
            paletteInput.SelectedIndex = details ? 0 : 4;
            paletteStepInput.Value = 16;
            spriteInput.IsChecked = details;
            alphaInput.Value = details ? 75 : 50;
            automaticInput.IsChecked = true;
            ditheringInput.IsChecked = false;
            cropHorizontalInput.SelectedIndex = cropVerticalInput.SelectedIndex = 0;
            brightnessInput.Value = contrastInput.Value = saturationInput.Value = edgeInput.Value = 50;
            profileState.Text = "Настройки режима";
            UpdateDependentControls();
            MarkResultStale();
            UpdateSizeHint();
            statusLabel.Text = $"Параметры режима «{CurrentModeName}» применены.";
        }
        finally { _applyingProfile = false; }
    }

    private void SettingsChanged(object? sender, EventArgs e)
    {
        if (_applyingProfile || _restoringHistory) return;
        UpdateDependentControls();
        profileState.Text = "Настройки изменены вручную";
        MarkResultStale();
        UpdateSizeHint();
    }

    private void UpdateDependentControls()
    {
        quantizationColorControls.IsEnabled = quantizationColorModeInput.IsChecked == true;
        paletteColorControls.IsEnabled = paletteColorModeInput.IsChecked == true;
        quantizationColorControls.Opacity = quantizationColorControls.IsEnabled ? 1 : .55;
        paletteColorControls.Opacity = paletteColorControls.IsEnabled ? 1 : .55;
        SetVisible(stepControls, paletteInput.SelectedIndex == 4);
        SetVisible(cropControls, spriteInput.IsChecked != true);
        SetVisible(alphaControls, spriteInput.IsChecked == true);
        bool ditheringAllowed = spriteInput.IsChecked != true && paletteInput.SelectedIndex is >= 0 and <= 3;
        ditheringInput.IsEnabled = paletteColorControls.IsEnabled && ditheringAllowed;
        if (!ditheringAllowed) ditheringInput.IsChecked = false;
        SetVisible(manualControls, manualInput.IsChecked == true);
        brightnessLabel.Text = $"Яркость · {brightnessInput.Value}";
        contrastLabel.Text = $"Контраст · {contrastInput.Value}";
        saturationLabel.Text = $"Насыщенность · {saturationInput.Value}";
        edgeLabel.Text = $"Контур · {edgeInput.Value}";
        localBrightnessLabel.Text = $"Яркость · {localBrightnessInput.Value}";
        localContrastLabel.Text = $"Контраст · {localContrastInput.Value}";
        localSaturationLabel.Text = $"Насыщенность · {localSaturationInput.Value}";
        localEdgeLabel.Text = $"Контур · {localEdgeInput.Value}";
    }

    private void SizeChangedByUser(bool widthChanged)
    {
        if (_updatingSize || _restoringHistory) return;
        _updatingSize = true;
        try
        {
            var sizeSource = operationSourceInput.SelectedIndex == 1 ? _downscaledResult : _sourceImage;
            if (aspectLock.IsChecked == true && sizeSource is not null)
            {
                var input = widthChanged ? heightInput : widthInput;
                double value = widthChanged
                    ? Math.Round((double)widthInput.Value * sizeSource.Height / sizeSource.Width)
                    : Math.Round((double)heightInput.Value * sizeSource.Width / sizeSource.Height);
                input.Value = (int)Math.Clamp(value, input.Minimum, input.Maximum);
            }
        }
        finally { _updatingSize = false; }
        MarkResultStale();
        UpdateSizeHint();
    }

    private void UpdateSizeHint()
    {
        var sizeSource = operationSourceInput.SelectedIndex == 1 ? _downscaledResult : _sourceImage;
        if (sizeSource is null)
        {
            sizeHint.Text = "Укажите ширину и высоту результата.";
            sizeToolGroup.ToolTip = sizeHint.Text;
            frameToolGroup.IsEnabled = pixelToolGroup.IsEnabled = true;
            return;
        }
        int width = (int)widthInput.Value, height = (int)heightInput.Value;
        if (width > sizeSource.Width || height > sizeSource.Height)
            sizeHint.Text = $"Основа {sizeSource.Width} × {sizeSource.Height}. Увеличение через «Изменить размер» сохраняет цвета и прозрачность без сглаживания. «Обработать» не увеличивает исходник.";
        else if (spriteInput.IsChecked == true && Math.Abs((double)width / height / ((double)sizeSource.Width / sizeSource.Height) - 1) > .02)
            sizeHint.Text = "Пропорции отличаются от исходника: весь кадр будет растянут. Включите сохранение пропорций.";
        else
            sizeHint.Text = spriteInput.IsChecked == true
                ? "Весь исходник попадёт в сетку. Порог заполнения регулирует края прозрачных объектов."
                : "Область исходника подгоняется обрезкой под целые блоки. Положение области задаётся ниже.";
        sizeToolGroup.ToolTip = sizeHint.Text;
        bool reducing = width <= sizeSource.Width && height <= sizeSource.Height;
        frameToolGroup.IsEnabled = pixelToolGroup.IsEnabled = reducing;
    }

    private void MarkResultStale()
    {
        if (_resultIsAlignment || _restoringHistory || _activeSource?.SelectedGeneration?.IsGridReduction == true) return;
        if (_downscaledResult is null) return;
        _resultStale = true;
        resultCaption.Text = $"РЕЗУЛЬТАТ · {_downscaledResult.Width} × {_downscaledResult.Height} · предыдущие настройки";
        statusLabel.Text = "Настройки изменены. Выберите нужную операцию, чтобы создать новый результат.";
    }

    private void UpdatePreviewSettings()
    {
        double zoom = ZoomFactors[Math.Clamp(zoomInput.SelectedIndex, 0, ZoomFactors.Length - 1)];
        foreach (var preview in new[] { sourcePreview, resultPreview })
        {
            preview.Zoom = zoom;
            preview.PreviewBackground = backgroundInput.SelectedIndex;
        }
    }

    private void UpdateSourcePreview()
    {
        sourcePreview.Image = _sourceImage;
        sourceCaption.Text = _sourceImage is null ? "ИСХОДНИК" :
            $"ИСХОДНИК · {_sourceImage.Width} × {_sourceImage.Height}";
    }

    private async void OpenImage(object? sender, EventArgs e)
    {
        if (_processing || openDialog.ShowDialog(this) != true) return;
        await AddImagesAsync(openDialog.FileNames);
        if (_sourceImage is not null) ShowEditor();
    }

    private async void ProcessImage(object? sender, EventArgs e)
    {
        if (_sourceImage is null || _activeSource is null || _processing) return;
        ApplyProfile();
        CommitInputs();
        var settings = CaptureReadyModeSettings();
        DownscaleOptions options = settings.Downscale;
        bool useResult = operationSourceInput.SelectedIndex == 1 && _downscaledResult is not null;
        var basis = useResult ? _downscaledResult! : _sourceImage;
        if (options.TargetWidth > basis.Width || options.TargetHeight > basis.Height)
        {
            statusLabel.Text = "Уменьшите размер результата: он не должен превышать размер выбранной основы.";
            return;
        }
        string mode = CurrentModeName;
        var document = _activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        using var source = (Bitmap)basis.Clone();
        SetProcessingState(true);
        statusLabel.Text = $"Обработка · {mode} · {options.TargetWidth} × {options.TargetHeight}…";
        try
        {
            var generation = await Task.Run(() =>
            {
                var result = _downscaler.Process(source, options);
                try
                {
                    return _library.SaveGeneration(document, result.Downscaled, result.CroppedSource, new GenerationEntry
                    {
                        Settings = settings,
                        ParentGenerationId = parentId,
                        PreservesTransparency = options.SpriteMode,
                        Caption = $"РЕЗУЛЬТАТ · {result.Downscaled.Width} × {result.Downscaled.Height} · {mode}",
                        Log = string.Join(Environment.NewLine, result.StageTimingsSeconds.Select(stage => $"{stage.Key}: {stage.Value:F3} с")) +
                            $"{Environment.NewLine}Основной цвет: {result.DominantColor}"
                    });
                }
                finally { result.CroppedSource.Dispose(); result.Downscaled.Dispose(); }
            });
            document.Generations.Add(generation);
            SelectGeneration(generation);
            statusLabel.Text = $"Готово · {generation.Width} × {generation.Height}. Результат сохранён в истории.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Обработка или сохранение истории не завершены. Предыдущий результат доступен.";
            MessageBox.Show(this, ex.Message, "Ошибка обработки", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetProcessingState(false); }
    }
    private void SetProcessingState(bool processing)
    {
        _processing = processing;
        UpdateCompactButton();
        settingsPanel.IsEnabled = !processing;
        foreach (var tool in _toolWindows.Values) tool.ToolContent.IsEnabled = !processing;
        foreach (var button in new[] { fileMenuButton, toolsMenuButton, gridToolButton, sizeToolButton,
            colorToolButton, smoothingToolButton, profileToolButton, filtersToolButton, infoToolButton, compactProfileToolButton,
            compactSizeToolButton, compactColorToolButton, compactSmoothingToolButton, compactGridToolButton,
            compactFiltersToolButton, compactInfoToolButton })
            button.IsEnabled = !processing;
        operationSourceInput.IsEnabled = !processing;
        sourceStrip.IsEnabled = generationStrip.IsEnabled = !processing;
        openButton.IsEnabled = !processing;
        homeContent.IsEnabled = !processing;
        backToLibraryButton.IsEnabled = !processing;
        processButton.IsEnabled = !processing && _sourceImage is not null;
        scaleOnlyButton.IsEnabled = colorOnlyButton.IsEnabled = neighborColorsButton.IsEnabled =
            !processing && _sourceImage is not null;
        var selectedGeneration = _activeSource?.SelectedGeneration;
        int generationNumber = selectedGeneration is null ? -1 : _activeSource!.Generations.IndexOf(selectedGeneration) + 1;
        ((ComboBoxItem)operationSourceInput.Items[1]).Content = generationNumber > 0
            ? $"Результат №{generationNumber}" : "Выбранный результат";
        ((ComboBoxItem)operationSourceInput.Items[1]).IsEnabled = !processing && _downscaledResult is not null;
        if (_downscaledResult is null && operationSourceInput.SelectedIndex == 1) operationSourceInput.SelectedIndex = 0;
        alignButton.IsEnabled = !processing && _sourceImage is not null;
        saveButton.IsEnabled = !processing && _downscaledResult is not null;
        saveAllButton.IsEnabled = !processing && _activeSource?.Generations.Count > 0;
        Cursor = processing ? Cursors.Wait : null;
        UpdateBusyIndicator();
        if (!processing) RefreshInfo();
        RefreshFilterState();
    }

    private void CommitInputs()
    {
        foreach (var input in new[] { widthInput, heightInput, gridCellInput, paletteStepInput, alphaInput, quantizationColorsInput, localColorPassesInput, threadsInput })
            input.Commit();
    }

    private DownscaleOptions BuildOptionsFromUi() => new()
    {
        TargetWidth = (int)widthInput.Value,
        TargetHeight = (int)heightInput.Value,
        SpriteMode = spriteInput.IsChecked == true,
        AlphaThreshold = (int)alphaInput.Value,
        CropHorizontal = (CropHorizontalAlignment)cropHorizontalInput.SelectedIndex,
        CropVertical = (CropVerticalAlignment)cropVerticalInput.SelectedIndex,
        Palette = _capturingReadyMode && detailsMode.IsChecked == true
            ? PaletteKind.None : (PaletteKind)(paletteInput.SelectedIndex + 1),
        PaletteStep = (int)paletteStepInput.Value,
        Quantization = (QuantizationMethod)quantizationInput.SelectedIndex,
        QuantizationColors = (int)quantizationColorsInput.Value,
        IndependentColorMode = paletteColorModeInput.IsChecked == true
            ? IndependentColorMode.Palette : IndependentColorMode.Quantization,
        LocalColorPasses = (int)localColorPassesInput.Value,
        LocalColorCriteria = new ManualBlockCriteria
        {
            TargetBrightness = localBrightnessInput.Value / 100.0,
            TargetContrast = localContrastInput.Value / 100.0,
            TargetSaturation = localSaturationInput.Value / 100.0,
            TargetEdge = localEdgeInput.Value / 100.0
        },
        UseColorWeights = colorWeightsInput.IsChecked == true,
        ThreadCount = (int)threadsInput.Value,
        EnableDithering = spriteInput.IsChecked != true && paletteInput.SelectedIndex is >= 0 and <= 3 && ditheringInput.IsChecked == true,
        BlockMode = manualInput.IsChecked == true ? BlockSelectionMode.Manual : BlockSelectionMode.Automatic,
        ManualCriteria = manualInput.IsChecked == true ? new ManualBlockCriteria
        {
            TargetBrightness = brightnessInput.Value / 100.0,
            TargetContrast = contrastInput.Value / 100.0,
            TargetSaturation = saturationInput.Value / 100.0,
            TargetEdge = edgeInput.Value / 100.0
        } : null
    };

    private void SaveImage(object? sender, EventArgs e)
    {
        if (_downscaledResult is null) return;
        string suffix = _resultIsAlignment ? "aligned" : _activeSource?.SelectedGeneration?.IsGridReduction == true ? "pixels" : _activeSource?.SelectedGeneration?.Operation == "Увеличение" ? "upscaled" : "pixelized";
        saveDialog.FileName = $"{Path.GetFileNameWithoutExtension(_activeSource?.Label ?? _selectedFilePath) ?? "result"}_{suffix}.png";
        saveDialog.Filter = _resultPreservesTransparency ? "PNG (с прозрачностью)|*.png"
            : "PNG|*.png|JPEG|*.jpg;*.jpeg|BMP|*.bmp";
        saveDialog.FilterIndex = 1;
        if (saveDialog.ShowDialog(this) != true) return;
        try { SaveResult(saveDialog.FileName); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка сохранения", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveAllImages(object sender, RoutedEventArgs e)
    {
        fileMenuPopup.IsOpen = false;
        if (_processing || _activeSource?.Generations.Count is not > 0) return;
        var folderDialog = new OpenFolderDialog { Title = "Сохранить все результаты текущего исходника" };
        if (folderDialog.ShowDialog(this) != true) return;

        SetProcessingState(true);
        statusLabel.Text = "Сохранение результатов…";
        try
        {
            var (saved, errors) = await Task.Run(() => SaveAllResults(folderDialog.FolderName));
            statusLabel.Text = errors.Count == 0
                ? $"Сохранено результатов: {saved} · {folderDialog.FolderName}"
                : $"Сохранено: {saved}. Ошибок: {errors.Count}.";
            if (errors.Count > 0)
                MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Не все результаты сохранены",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Не удалось сохранить результаты.";
            MessageBox.Show(this, ex.Message, "Ошибка сохранения", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetProcessingState(false); }
    }

    private (int Saved, List<string> Errors) SaveAllResults(string directory)
    {
        var source = _activeSource ?? throw new InvalidOperationException("Выберите исходник.");
        var generations = source.Generations.ToArray();
        if (generations.Length == 0) throw new InvalidOperationException("У исходника пока нет результатов.");
        string outputDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(outputDirectory);
        string sourceName = Path.GetFileNameWithoutExtension(source.Label);
        if (string.IsNullOrWhiteSpace(sourceName)) sourceName = "image";
        if (sourceName.Length > 80) sourceName = sourceName[..80];

        int saved = 0;
        var errors = new List<string>();
        for (int q = 0; q < generations.Length; q++)
        {
            var generation = generations[q];
            string original = _library.ResultPath(source, generation);
            string stem = $"{sourceName}_result_{q + 1:D3}_{generation.Id.ToString("N")[..8]}";
            string temporary = Path.Combine(outputDirectory, $".{stem}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.Copy(original, temporary);
                for (int copy = 1; ; copy++)
                {
                    string suffix = copy == 1 ? "" : $"_{copy}";
                    string destination = Path.Combine(outputDirectory, stem + suffix + ".png");
                    try
                    {
                        File.Move(temporary, destination);
                        saved++;
                        break;
                    }
                    catch (IOException) when (File.Exists(destination)) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{generation.Label}: {ex.Message}");
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return (saved, errors);
    }

    private void SaveResult(string path)
    {
        if (_downscaledResult is null) throw new InvalidOperationException("Сначала обработайте изображение.");
        string outputPath = Path.GetFullPath(path);
        string extension = Path.GetExtension(outputPath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp") || (_resultPreservesTransparency && extension != ".png"))
        {
            throw new InvalidOperationException(_resultPreservesTransparency
                ? "Сохраните этот результат в PNG, чтобы сохранить прозрачность."
                : "Выберите формат PNG, JPEG или BMP.");
        }
        if (string.Equals(outputPath, _selectedFilePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Выберите другое имя: исходное изображение нужно сохранить.");
        }
        string temporaryPath = Path.Combine(Path.GetDirectoryName(outputPath)!, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var format = extension switch
            {
                ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                ".bmp" => ImageFormat.Bmp,
                _ => ImageFormat.Png
            };
            _downscaledResult.Save(temporaryPath, format);
            File.Move(temporaryPath, outputPath, overwrite: true);
            statusLabel.Text = $"Сохранено: {outputPath}" + (_resultStale ? " · результат предыдущих настроек" : "");
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private void AlignmentSettingsChanged()
    {
        if (_restoringHistory || !_resultIsAlignment || _downscaledResult is null) return;
        _resultStale = true;
        resultCaption.Text = $"ВЫРОВНЕННАЯ СЕТКА · {_downscaledResult.Width} × {_downscaledResult.Height} · предыдущие настройки";
        statusLabel.Text = "Размер ячейки изменён. Нажмите «Выровнять сетку».";
    }

    private async void AlignImage(object? sender, EventArgs e)
    {
        if (_sourceImage is null || _activeSource is null || _processing) return;
        CommitInputs();
        var settings = CaptureSettings();
        var options = new GridAlignmentOptions { CellSize = settings.DetectGrid ? null : settings.CellSize };
        bool useResult = operationSourceInput.SelectedIndex == 1 && _downscaledResult is not null;
        var basis = useResult ? _downscaledResult! : _sourceImage;
        var document = _activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        using var source = (Bitmap)basis.Clone();
        SetProcessingState(true);
        statusLabel.Text = "Выравнивание пиксельной сетки…";
        try
        {
            var generation = await Task.Run(() =>
            {
                using var result = new PixelGridAligner().Align(source, options);
                return _library.SaveGeneration(document, result.Aligned, null, new GenerationEntry
                {
                    Settings = settings, IsAlignment = true, PreservesTransparency = true, CellSize = result.CellSize,
                    ParentGenerationId = parentId,
                    Caption = $"ВЫРОВНЕННАЯ СЕТКА · {result.Aligned.Width} × {result.Aligned.Height} · ячейка {result.CellSize} px",
                    Log = $"Выравнивание: {result.ElapsedSeconds:F3} с. Ячейка: {result.CellSize} px."
                });
            });
            document.Generations.Add(generation);
            SelectGeneration(generation);
            statusLabel.Text = $"Готово. Ячейка {generation.CellSize} px. Результат сохранён в истории.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Выравнивание или сохранение истории не завершены.";
            MessageBox.Show(this, ex.Message, "Выравнивание сетки", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally { SetProcessingState(false); }
    }
}
