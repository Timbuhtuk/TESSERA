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

    private readonly OpenFileDialog openDialog = new() { Multiselect = true };
    private readonly SaveFileDialog saveDialog = new() { DefaultExt = ".png", AddExtension = true, OverwritePrompt = true };

    public MainWindow() : this(LibraryLocation.PrepareDefault(), AppearanceSettings.DefaultPath) { _autoCheckUpdates = true; }

    public MainWindow(string libraryDirectory, string? appearancePath = null)
    {
        _appearancePath = appearancePath ?? Path.Combine(libraryDirectory, "appearance.json");
        _appearanceSettings = AppearanceSettings.Load(_appearancePath);
        Localization.Initialize(_appearanceSettings.Language);
        openDialog.Filter = Localization.Text("Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.gif");
        openDialog.Title = Localization.Text("Добавить изображения");
        saveDialog.Title = Localization.Text("Сохранить результат");
        AppearanceResources.Apply(_appearanceSettings.Theme);
        _library = new ImageLibrary(libraryDirectory);
        InitializeComponent();
        InitializeChannelTool();
        InitializeHistoryTrays();
        InitializeMacros();
        InitializeNavigation();
        InitializePreviewMode();
        InitializeWindowFrame();
        threadsInput.Maximum = threadsInput.Value = Math.Max(1, Environment.ProcessorCount);
        foreach (var mode in new[] { backgroundMode, sceneMode, detailsMode })
            mode.Checked += (_, _) => UpdateModeDescription();
        foreach (var box in new[] { paletteInput, quantizationInput, cropHorizontalInput, cropVerticalInput })
            box.SelectionChanged += SettingsChanged;
        foreach (var input in new[] { paletteStepInput, alphaInput, quantizationColorsInput, localColorPassesInput })
            input.ValueChanged += SettingsChanged;
        foreach (var check in new ToggleButton[] { spriteInput, ditheringInput, colorWeightsInput, automaticInput, manualInput,
            quantizationColorModeInput, paletteColorModeInput, localDetailGuardInput })
        {
            check.Checked += SettingsChanged;
            check.Unchecked += SettingsChanged;
        }
        foreach (var slider in new[] { brightnessInput, contrastInput, saturationInput, edgeInput,
            localBrightnessInput, localContrastInput, localEdgeInput })
            slider.ValueChanged += (_, _) => SettingsChanged(null, EventArgs.Empty);
        widthInput.ValueChanged += (_, _) => SizeChangedByUser(widthChanged: true);
        heightInput.ValueChanged += (_, _) => SizeChangedByUser(widthChanged: false);
        aspectLock.Checked += (_, _) => SizeChangedByUser(widthChanged: true);
        detectGridInput.Checked += (_, _) => { gridCellInput.IsEnabled = false; AlignmentSettingsChanged(); };
        detectGridInput.Unchecked += (_, _) => { gridCellInput.IsEnabled = true; AlignmentSettingsChanged(); };
        gridCellInput.ValueChanged += (_, _) => AlignmentSettingsChanged();
        globalGridModeInput.Checked += (_, _) => UpdateGridAlignmentMode();
        localGridModeInput.Checked += (_, _) => UpdateGridAlignmentMode();
        snapLocalGridInput.Checked += (_, _) => UpdateGridAlignmentMode();
        snapLocalGridInput.Unchecked += (_, _) => UpdateGridAlignmentMode();
        UpdateGridAlignmentMode();
        processButton.Click += ProcessImage;
        scaleOnlyButton.Click += ScaleOnlyImage;
        trimTransparentButton.Click += TrimTransparentEdges;
        colorOnlyButton.Click += ApplyColorsOnly;
        neighborColorsButton.Click += ApplyNeighborColorsOnly;
        alignButton.Click += AlignImage;
        Closing += (_, e) => { if (_workspaceImporting || _processing || iconWorkspace.IsBusy || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy || moodboardWorkspace.IsBusy) { e.Cancel = true; statusLabel.Text = Localization.Text("Дождитесь завершения обработки."); } else if (!moodboardWorkspace.CanLeave()) e.Cancel = true; };
        Closed += (_, _) =>
        {
            CloseEditorTools();
            _restoringHistory = true;
            sourcePreview.Image = resultPreview.Image = null;
            _sourceImage?.Dispose(); _sourceImage = null;
            _downscaledResult?.Dispose(); _downscaledResult = null;
        };
        sceneMode.IsChecked = true;
        InitializeCleanupControls();
        InitializeLibrary();
        InitializeImageActions();
        InitializeLiveToolPreviews();
        InitializeContourPreview();
        InitializeHome();
        InitializeIcons();
        InitializeAseprite();
        InitializeBackground();
        InitializeMoodboard();
        InitializeInfo();
        InitializePalettePreviews();
        InitializeFilters();
        InitializeUpdates();
        UpdatePreviewSettings();
        UpdateDependentControls();
        if (LibraryLocation.MigrationWarning is { } warning)
            statusLabel.Text = Localization.Format("Не удалось полностью перенести старую историю: {0}", warning);
    }

    private string CurrentModeName => detailsMode.IsChecked == true ? Localization.Text("Детали") : backgroundMode.IsChecked == true ? Localization.Text("Фон") : Localization.Text("Сцена");
    private static void SetVisible(UIElement control, bool visible) => control.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    private void UpdateModeDescription()
    {
        modeDescription.Text = detailsMode.IsChecked == true
            ? Localization.Text("Пиксель-арт и спрайты: приоритет контуров и прозрачности. При уменьшении часть деталей может исчезнуть.")
            : backgroundMode.IsChecked == true
                ? Localization.Text("Крупные фоновые изображения: цельные цветовые области и общая композиция, без акцента на мелочах.")
                : Localization.Text("Сцены средней детализации: сохраняем значимые формы, свет и цветовые переходы.");
        profileState.Text = Localization.Text("Параметры режима применятся при обработке");
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
            profileState.Text = Localization.Text("Настройки режима");
            UpdateDependentControls();
            MarkResultStale();
            UpdateSizeHint();
            statusLabel.Text = Localization.Format("Параметры режима «{0}» применены.", CurrentModeName);
        }
        finally { _applyingProfile = false; }
    }

    private void SettingsChanged(object? sender, EventArgs e)
    {
        if (_applyingProfile || _restoringHistory) return;
        UpdateDependentControls();
        profileState.Text = Localization.Text("Настройки изменены вручную");
        MarkResultStale();
        UpdateSizeHint();
    }

    private void UpdateDependentControls()
    {
        bool quantizationSelected = quantizationColorModeInput.IsChecked == true;
        bool paletteSelected = paletteColorModeInput.IsChecked == true;
        quantizationColorControls.IsEnabled = quantizationSelected;
        paletteColorControls.IsEnabled = paletteSelected;
        quantizationColorControls.Opacity = quantizationSelected ? 1 : .55;
        paletteColorControls.Opacity = paletteSelected ? 1 : .55;
        SetVisible(stepControls, paletteInput.SelectedIndex == 4);
        SetVisible(cropControls, spriteInput.IsChecked != true);
        SetVisible(alphaControls, spriteInput.IsChecked == true);
        bool ditheringAllowed = paletteInput.SelectedIndex is >= 0 and <= 3;
        ditheringInput.IsEnabled = paletteSelected && ditheringAllowed;
        if (!ditheringAllowed) ditheringInput.IsChecked = false;
        SetVisible(manualControls, manualInput.IsChecked == true);
        brightnessLabel.Text = Localization.Format("Яркость · {0}", brightnessInput.Value);
        contrastLabel.Text = Localization.Format("Контраст · {0}", contrastInput.Value);
        saturationLabel.Text = Localization.Format("Насыщенность · {0}", saturationInput.Value);
        edgeLabel.Text = Localization.Format("Контур · {0}", edgeInput.Value);
        localBrightnessLabel.Text = Localization.Format("Сходство цветов · {0:0.#} ΔE", localBrightnessInput.Value * .4);
        localContrastLabel.Text = Localization.Format("Сила очистки · {0:0}%", localContrastInput.Value);
        localEdgeLabel.Text = Localization.Format("Защита границ · {0:0}%", localEdgeInput.Value);
    }

    private void SizeChangedByUser(bool widthChanged)
    {
        if (_updatingSize || _restoringHistory) return;
        _updatingSize = true;
        try
        {
            var sizeSource = ProcessingBasis == 1 ? _downscaledResult : _sourceImage;
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
        UpdateCropPreview();
        var sizeSource = ProcessingBasis == 1 ? _downscaledResult : _sourceImage;
        if (sizeSource is null)
        {
            sizeHint.Text = Localization.Text("Укажите ширину и высоту результата.");
            sizeToolGroup.ToolTip = sizeHint.Text;
            frameToolGroup.IsEnabled = pixelToolGroup.IsEnabled = true;
            return;
        }
        int width = (int)widthInput.Value, height = (int)heightInput.Value;
        if (width > sizeSource.Width || height > sizeSource.Height)
            sizeHint.Text = Localization.Format("Основа {0} × {1}. Увеличение через «Изменить размер» сохраняет цвета и прозрачность без сглаживания. «Обработать» не увеличивает исходник.", sizeSource.Width, sizeSource.Height);
        else if (spriteInput.IsChecked == true && Math.Abs((double)width / height / ((double)sizeSource.Width / sizeSource.Height) - 1) > .02)
            sizeHint.Text = Localization.Text("Пропорции отличаются от исходника: весь кадр будет растянут. Включите сохранение пропорций.");
        else
            sizeHint.Text = spriteInput.IsChecked == true
                ? Localization.Text("Весь исходник попадёт в сетку. Порог заполнения регулирует края прозрачных объектов.")
                : Localization.Text("Область исходника подгоняется обрезкой под целые блоки. Положение области задаётся ниже.");
        sizeToolGroup.ToolTip = sizeHint.Text;
        bool reducing = width <= sizeSource.Width && height <= sizeSource.Height;
        frameToolGroup.IsEnabled = pixelToolGroup.IsEnabled = reducing;
    }

    private void MarkResultStale()
    {
        if (_resultIsAlignment || _restoringHistory || _activeSource?.SelectedGeneration?.IsGridReduction == true) return;
        if (_downscaledResult is null) return;
        _resultStale = true;
        resultCaption.Text = Localization.Format("РЕЗУЛЬТАТ · {0} × {1} · предыдущие настройки", _downscaledResult.Width, _downscaledResult.Height);
        statusLabel.Text = Localization.Text("Настройки изменены. Выберите нужную операцию, чтобы создать новый результат.");
    }

    private void UpdatePreviewSettings()
    {
        double zoom = ZoomFactors[Math.Clamp(PreviewZoom, 0, ZoomFactors.Length - 1)];
        foreach (var preview in new[] { sourcePreview, resultPreview })
        {
            preview.Zoom = zoom;
            preview.PreviewBackground = PreviewBackground;
        }
        UpdateComparisonPreview();
    }

    private void UpdateSourcePreview()
    {
        sourcePreview.Image = _sourceImage;
        if (_workspaceCatalog?.State.Projects.FirstOrDefault(p => p.Id == _workspaceSourceProject) is { } project)
            BuildSourceBreadcrumb(project);
        else sourceCaption.Text = _sourceImage is null ? Localization.Text("ИСХОДНИК") :
            Localization.Format("ИСХОДНИК · {0} × {1}", _sourceImage.Width, _sourceImage.Height);
        UpdateComparisonPreview();
    }

    private async void OpenImage(object? sender, EventArgs e)
    {
        if (_workspaceReady && _workspaceBrowser?.Visibility == Visibility.Visible) { await PromptWorkspaceFilesAsync(); return; }
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
        bool useResult = ProcessingBasis == 1 && _downscaledResult is not null;
        var basis = useResult ? _downscaledResult! : _sourceImage;
        if (options.TargetWidth > basis.Width || options.TargetHeight > basis.Height)
        {
            statusLabel.Text = Localization.Text("Уменьшите размер результата: он не должен превышать размер выбранной основы.");
            return;
        }
        string mode = CurrentModeName;
        var document = _activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        using var source = (Bitmap)basis.Clone();
        SetProcessingState(true);
        statusLabel.Text = Localization.Format("Обработка · {0} · {1} × {2}…", mode, options.TargetWidth, options.TargetHeight);
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
                        OperationId = ProcessingOperations.Profile,
                        AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = ProcessingOperations.Profile, Downscale = options }),
                        ParentGenerationId = parentId,
                        PreservesTransparency = options.SpriteMode || System.Drawing.Image.IsAlphaPixelFormat(result.Downscaled.PixelFormat),
                        Caption = Localization.Format("РЕЗУЛЬТАТ · {0} × {1} · {2}", result.Downscaled.Width, result.Downscaled.Height, mode),
                        Log = string.Join(Environment.NewLine, result.StageTimingsSeconds.Select(stage => Localization.Format("{0}: {1:F3} с", stage.Key, stage.Value))) +
                            Localization.Format("{0}Основной цвет: {1}", Environment.NewLine, result.DominantColor)
                    });
                }
                finally { result.CroppedSource.Dispose(); result.Downscaled.Dispose(); }
            });
            document.Generations.Add(generation);
            SelectGeneration(generation);
            statusLabel.Text = Localization.Format("Готово · {0} × {1}. Результат сохранён в истории.", generation.Width, generation.Height);
        }
        catch (Exception ex)
        {
            statusLabel.Text = Localization.Text("Обработка или сохранение истории не завершены. Предыдущий результат доступен.");
            TesseraDialog.Show(this, ex.Message, Localization.Text("Ошибка обработки"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetProcessingState(false); }
    }
    private void SetProcessingState(bool processing)
    {
        _processing = processing;
        UpdateCompactButton();
        settingsPanel.IsEnabled = !processing;
        if (_workspaceToolContent is not null) _workspaceToolContent.IsEnabled = !processing || _macroCancellation is not null;
        foreach (var tool in _toolWindows.Values) tool.ToolContent.IsEnabled = !processing;
        sourceStrip.IsEnabled = generationStrip.IsEnabled = !processing;
        homeContent.IsEnabled = !processing;
        processButton.IsEnabled = !processing && _sourceImage is not null;
        scaleOnlyButton.IsEnabled = colorOnlyButton.IsEnabled = neighborColorsButton.IsEnabled =
            !processing && _sourceImage is not null;
        trimTransparentButton.IsEnabled = !processing && _sourceImage is not null;
        contourApplyButton.IsEnabled = contourPreviewButton.IsEnabled = !processing && LivePreviewBasis is not null;
        if (processing) ClearContourPreview();
        if (!processing) ScheduleLiveToolPreviews();
        if (_downscaledResult is null && ProcessingBasis == 1) ProcessingBasis = 0;
        alignButton.IsEnabled = !processing && _sourceImage is not null;
        Cursor = processing ? Cursors.Wait : null;
        UpdateBusyIndicator();
        if (!processing) RefreshInfo();
        RefreshFilterState();
        UpdateNavigationState();
        if (!processing) SynchronizeWorkspace();
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
        Cleanup = new ColorCleanupOptions
        {
            ColorTolerance = localBrightnessInput.Value * .4,
            Strength = localContrastInput.Value / 100.0,
            DetailProtection = localDetailGuardInput.IsChecked == true ? new ColorCleanupOptions().DetailProtection : 0,
            EdgeProtection = localEdgeInput.Value / 100.0
        },
        UseColorWeights = colorWeightsInput.IsChecked == true,
        ThreadCount = (int)threadsInput.Value,
        EnableDithering = (!_capturingReadyMode || spriteInput.IsChecked != true) && paletteInput.SelectedIndex is >= 0 and <= 3 && ditheringInput.IsChecked == true,
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
        string suffix = _resultIsAlignment ? "aligned" : _activeSource?.SelectedGeneration?.IsGridReduction == true ? "pixels" : _activeSource?.SelectedGeneration?.Operation == Localization.Text("Увеличение") ? "upscaled" : "pixelized";
        saveDialog.FileName = $"{Path.GetFileNameWithoutExtension(_activeSource?.Label ?? _selectedFilePath) ?? "result"}_{suffix}.png";
        saveDialog.Filter = _resultPreservesTransparency ? Localization.Text("PNG (с прозрачностью)|*.png")
            : "PNG|*.png|JPEG|*.jpg;*.jpeg|BMP|*.bmp";
        saveDialog.FilterIndex = 1;
        if (saveDialog.ShowDialog(this) != true) return;
        try { SaveResult(saveDialog.FileName); }
        catch (Exception ex)
        {
            TesseraDialog.Show(this, ex.Message, Localization.Text("Ошибка сохранения"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveAllImages(object sender, RoutedEventArgs e)
    {
        CloseNavigationMenus();
        if (_processing || _activeSource?.Generations.Count is not > 0) return;
        var folderDialog = new OpenFolderDialog { Title = Localization.Text("Сохранить все результаты текущего исходника") };
        if (folderDialog.ShowDialog(this) != true) return;

        SetProcessingState(true);
        statusLabel.Text = Localization.Text("Сохранение результатов…");
        try
        {
            var (saved, errors) = await Task.Run(() => SaveAllResults(folderDialog.FolderName));
            statusLabel.Text = errors.Count == 0
                ? Localization.Format("Сохранено результатов: {0} · {1}", saved, folderDialog.FolderName)
                : Localization.Format("Сохранено: {0}. Ошибок: {1}.", saved, errors.Count);
            if (errors.Count > 0)
                TesseraDialog.Show(this, string.Join(Environment.NewLine, errors), Localization.Text("Не все результаты сохранены"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            statusLabel.Text = Localization.Text("Не удалось сохранить результаты.");
            TesseraDialog.Show(this, ex.Message, Localization.Text("Ошибка сохранения"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetProcessingState(false); }
    }

    private (int Saved, List<string> Errors) SaveAllResults(string directory)
    {
        var source = _activeSource ?? throw new InvalidOperationException(Localization.Text("Выберите исходник."));
        var generations = source.Generations.ToArray();
        if (generations.Length == 0) throw new InvalidOperationException(Localization.Text("У исходника пока нет результатов."));
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
        if (_downscaledResult is null) throw new InvalidOperationException(Localization.Text("Сначала обработайте изображение."));
        string outputPath = Path.GetFullPath(path);
        string extension = Path.GetExtension(outputPath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp") || (_resultPreservesTransparency && extension != ".png"))
        {
            throw new InvalidOperationException(_resultPreservesTransparency
                ? Localization.Text("Сохраните этот результат в PNG, чтобы сохранить прозрачность.")
                : Localization.Text("Выберите формат PNG, JPEG или BMP."));
        }
        EnsureExportPathAllowed(outputPath);
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
            statusLabel.Text = Localization.Format("Сохранено: {0}", outputPath) + (_resultStale ? Localization.Text(" · результат предыдущих настроек") : "");
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private void EnsureExportPathAllowed(string path)
    {
        string output = Path.GetFullPath(path);
        string library = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_library.DirectoryPath));
        if (string.Equals(output, library, StringComparison.OrdinalIgnoreCase) ||
            output.StartsWith(library + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Localization.Text("Выберите файл вне внутренней библиотеки Tessera."));
        var sources = _sources.Select(source => source.OriginalPath).Concat(iconWorkspace.SourcePaths)
            .Concat(backgroundWorkspace.SourcePaths).Concat(moodboardWorkspace.SourcePaths)
            .Concat(_tileInspector?.SourcePaths ?? []).Concat(_closedTileSourcePaths);
        if (sources.Any(source => !string.IsNullOrWhiteSpace(source) &&
            string.Equals(output, Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(Localization.Text("Выберите другое имя: исходные изображения нужно сохранить."));
    }

    private GridAlignmentMode SelectedGridAlignmentMode => localGridModeInput.IsChecked == true ? GridAlignmentMode.Local : GridAlignmentMode.Global;

    private void UpdateGridAlignmentMode()
    {
        bool local = SelectedGridAlignmentMode == GridAlignmentMode.Local;
        gridCellInput.Maximum = local ? 64 : 256;
        gridCellInput.Value = Math.Min(gridCellInput.Value, gridCellInput.Maximum);
        globalGridHint.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        bool snap = snapLocalGridInput.IsChecked == true;
        snapLocalGridInput.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
        localGridHint.Visibility = local && !snap ? Visibility.Visible : Visibility.Collapsed;
        snappedGridHint.Visibility = local && snap ? Visibility.Visible : Visibility.Collapsed;
        AlignmentSettingsChanged();
    }

    private void AlignmentSettingsChanged()
    {
        if (_restoringHistory || !_resultIsAlignment || _downscaledResult is null) return;
        _resultStale = true;
        resultCaption.Text = Localization.Format("ВЫРОВНЕННАЯ СЕТКА · {0} × {1} · предыдущие настройки", _downscaledResult.Width, _downscaledResult.Height);
        statusLabel.Text = Localization.Text("Параметры сетки изменены. Нажмите «Выровнять сетку».");
    }

    private async void AlignImage(object? sender, EventArgs e)
    {
        if (_sourceImage is null || _activeSource is null || _processing) return;
        CommitInputs();
        var settings = CaptureSettings();
        var options = new GridAlignmentOptions { CellSize = settings.DetectGrid ? null : settings.CellSize,
            Mode = settings.AlignmentMode, SnapToGlobalGrid = settings.SnapToGlobalGrid };
        string operation = settings.AlignmentMode == GridAlignmentMode.Global ? ProcessingOperations.Align
            : settings.SnapToGlobalGrid ? ProcessingOperations.AlignLocalSnapped : ProcessingOperations.AlignLocal;
        bool useResult = ProcessingBasis == 1 && _downscaledResult is not null;
        var basis = useResult ? _downscaledResult! : _sourceImage;
        var document = _activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        using var source = (Bitmap)basis.Clone();
        SetProcessingState(true);
        statusLabel.Text = Localization.Text("Выравнивание пиксельной сетки…");
        try
        {
            var generation = await Task.Run(() =>
            {
                using var result = new PixelGridAligner().Align(source, options);
                return _library.SaveGeneration(document, result.Aligned, null, new GenerationEntry
                {
                    OperationId = operation,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = operation, CellSize = options.CellSize }),
                    Settings = settings, IsAlignment = true, PreservesTransparency = true, CellSize = result.CellSize,
                    ParentGenerationId = parentId,
                    Caption = Localization.Format(settings.AlignmentMode == GridAlignmentMode.Local
                        ? settings.SnapToGlobalGrid ? "ЛОКАЛЬНАЯ СЕТКА С ПРИВЯЗКОЙ · {0} × {1} · ячейка {2} px"
                            : "ЛОКАЛЬНАЯ СЕТКА · {0} × {1} · ячейка {2} px" : "ВЫРОВНЕННАЯ СЕТКА · {0} × {1} · ячейка {2} px",
                        result.Aligned.Width, result.Aligned.Height, result.CellSize),
                    Log = settings.AlignmentMode == GridAlignmentMode.Local
                        ? Localization.Format("Локальное выравнивание: {0:F3} с. Ячейка: {1} px. Областей: {2}.", result.ElapsedSeconds, result.CellSize, result.LocalRegionCount)
                            + (settings.SnapToGlobalGrid ? "\n" + Localization.Format("Привязка к общей сетке: сдвинуто областей {0}, конфликтов ячеек {1}.",
                                result.ShiftedRegionCount, result.ConflictingCellCount) : "")
                        : Localization.Format("Выравнивание: {0:F3} с. Ячейка: {1} px.", result.ElapsedSeconds, result.CellSize)
                });
            });
            document.Generations.Add(generation);
            SelectGeneration(generation);
            statusLabel.Text = Localization.Format("Готово. Ячейка {0} px. Результат сохранён в истории.", generation.CellSize);
        }
        catch (Exception ex)
        {
            statusLabel.Text = Localization.Text("Выравнивание или сохранение истории не завершены.");
            TesseraDialog.Show(this, ex.Message, Localization.Text("Выравнивание сетки"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally { SetProcessingState(false); }
    }
}
