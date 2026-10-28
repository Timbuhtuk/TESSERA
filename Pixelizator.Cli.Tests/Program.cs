using System.Drawing.Imaging;
using System.Text.Json;
using PixelArtAlignment;
using PixelArtDownscale;
using PixelArtWorkflows;
using Pixelizator.Cli;

// Standalone integration checks: no test runner or NuGet packages required.
string directory = Path.Combine(Path.GetTempPath(), "Pixelizator.Cli.Tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
int passed = 0;
int failed = 0;
try
{
    DarkContourChecks.Run(directory, Check);
    ConnectedPaletteChecks.Run(directory, Check);
    MacroToolsChecks.Run(directory, Check);
    StructureProbeChecks.Run(directory, Check);
    string input = Path.Combine(directory, "исходное изображение.png");
    using (var fixture = new Bitmap(67, 61, PixelFormat.Format32bppArgb))
    {
        for (int y = 0; y < fixture.Height; y++)
            for (int x = 0; x < fixture.Width; x++)
                fixture.SetPixel(x, y, Color.FromArgb((x * 7 + y * 11) % 256,
                    (x * 23 + y * 3) % 256, (x * 13 + y * 19) % 256));
        fixture.Save(input, ImageFormat.Png);
    }

    Check("LAB palette index matches exhaustive search including duplicate ties", () =>
    {
        var random = new Random(6006);
        var palette = Enumerable.Range(0, 1024).Select(_ => random.Next(0x1000000)).ToArray();
        palette[500] = palette[0];
        var type = typeof(ColorQuantizer).Assembly.GetType("PixelArtDownscale.LabPaletteIndex")!;
        var index = Activator.CreateInstance(type, [palette])!;
        var nearest = type.GetMethod("Nearest")!;
        foreach (int color in palette.Take(50).Concat(Enumerable.Range(0, 150).Select(_ => random.Next(0x1000000))))
        {
            int expected = 0;
            double best = ColorSpace.LabDistance(color, palette[0]);
            for (int i = 1; i < palette.Length; i++)
            {
                double distance = ColorSpace.LabDistance(color, palette[i]);
                if (distance < best) { best = distance; expected = i; }
            }
            Require((int)nearest.Invoke(index, [color])! == expected, "Nearest colour or tie breaking changed.");
        }
    });

    Check("Help and version do not process files", () =>
    {
        foreach (string[] arguments in new[] { Array.Empty<string>(), new[] { "--help" }, new[] { "-h" }, new[] { "--version" } })
        {
            var run = Run(arguments);
            Require(run.Code == 0 && run.Error.Length == 0 && run.Output.Length > 0, "Missing help/version");
        }
    });

    Check("ICO command exports selected and default frame sets", () =>
    {
        string icon = Path.Combine(directory, "application.ico");
        var run = Run(["ico", input, "-o", icon, "--sizes", "256,16,32",
            "--resize", "nearest-neighbor", "--fit", "cover", "--json"]);
        Require(run.Code == 0 && run.Error.Length == 0, run.Error);
        Require(ReadIcoSizes(icon).SequenceEqual(new[] { 16, 32, 256 }), "ICO sizes or sorting changed");
        using (var report = JsonDocument.Parse(run.Output))
        {
            Require(report.RootElement.GetProperty("process").GetString() == "icon-export", "Wrong ICO process");
            Require(report.RootElement.GetProperty("resize").GetString() == "nearest-neighbor", "Wrong resize report");
            Require(report.RootElement.GetProperty("fit").GetString() == "cover", "Wrong fit report");
        }

        Require(Run(["ico", input, "-o", icon]).Code == 1, "Existing ICO accepted");
        Require(Run(["ico", input, "-o", icon, "--sizes", "24", "--overwrite"]).Code == 0, "ICO replacement failed");
        Require(ReadIcoSizes(icon).SequenceEqual(new[] { 24 }), "Replacement retained old frames");

        string defaultInput = Path.Combine(directory, "default-icon.png");
        File.Copy(input, defaultInput);
        Require(Run(["icon", defaultInput]).Code == 0, "Default ICO export or alias failed");
        Require(ReadIcoSizes(Path.ChangeExtension(defaultInput, ".ico")).SequenceEqual(
            new[] { 16, 24, 32, 48, 64, 128, 256 }), "Default ICO sizes changed");
        Require(Run(["ico", "--help"]).Code == 0 && Run(["icon", "--help"]).Code == 0, "ICO help failed");
    });

    Check("ICO command rejects invalid parameters and reports file failures", () =>
    {
        string output = Path.Combine(directory, "invalid.ico");
        string[][] invalid =
        [
            ["--sizes", "0"], ["--sizes", "257"], ["--sizes", "16,16"], ["--sizes", "16,"],
            ["--sizes", "16.5"], ["--resize", "point"], ["--fit", "center"],
            ["--overwrite=true"], ["--json=true"], ["--unknown"], ["--palette", "none"],
            ["-o", Path.Combine(directory, "invalid.png")]
        ];
        foreach (var extra in invalid)
        {
            var run = Run(["ico", input, "-o", output, .. extra]);
            Require(run.Code == 2 && run.Output.Length == 0 && run.Error.Length > 0,
                $"Expected ICO usage error for {string.Join(' ', extra)}; got {run.Code}: {run.Error}");
            Require(!File.Exists(output), "Invalid ICO arguments created output");
        }

        Require(Run(["ico"]).Code == 0, "ICO command without arguments should show help");
        Require(Run(["ico", Path.Combine(directory, "missing.png"), "-o", output]).Code == 1, "Missing ICO input");
        string corrupt = Path.Combine(directory, "bad-icon-source.png");
        File.WriteAllText(corrupt, "not an image");
        Require(Run(["ico", corrupt, "-o", output]).Code == 1, "Corrupt ICO input");
    });
    Check("Background command exposes global and edge-connected removal", () =>
    {
        string sourcePath = Path.Combine(directory, "background-source.png");
        using (var fixture = new Bitmap(7, 7, PixelFormat.Format32bppArgb))
        {
            using var graphics = Graphics.FromImage(fixture);
            graphics.Clear(Color.White);
            for (int y = 2; y <= 4; y++)
                for (int x = 2; x <= 4; x++)
                    if (x is 2 or 4 || y is 2 or 4) fixture.SetPixel(x, y, Color.Black);
            fixture.Save(sourcePath, ImageFormat.Png);
        }

        string edgeOutput = Path.Combine(directory, "background-edges.png");
        var edgeRun = Run(["remove-background", sourcePath, "-o", edgeOutput, "--mode", "edges",
            "--background-color", "#FFFFFF", "--tolerance", "0", "--json"]);
        Require(edgeRun.Code == 0 && edgeRun.Error.Length == 0, edgeRun.Error);
        using (var result = new Bitmap(edgeOutput))
        {
            Require(result.Size == new Size(7, 7), "Background removal changed the canvas");
            Require(result.GetPixel(0, 0).A == 0, "Edge background remained");
            Require(result.GetPixel(3, 3).A == 255, "Edge mode removed an enclosed matching color");
            Require(result.GetPixel(2, 2).ToArgb() == Color.Black.ToArgb(), "Foreground changed");
        }
        using (var report = JsonDocument.Parse(edgeRun.Output))
        {
            Require(report.RootElement.GetProperty("process").GetString() == "background-removal", "Wrong background process");
            Require(report.RootElement.GetProperty("mode").GetString() == "edges", "Wrong background mode report");
            Require(report.RootElement.GetProperty("backgroundColor").GetString() == "#FFFFFF", "Wrong color report");
            Require(report.RootElement.GetProperty("tolerance").GetInt32() == 0, "Wrong tolerance report");
        }

        string globalOutput = Path.Combine(directory, "background-global.png");
        ExpectSuccess(["remove-bg", sourcePath, "-o", globalOutput, "--mode", "global",
            "--background-color", "white", "--tolerance", "0"]);
        using (var result = new Bitmap(globalOutput))
            Require(result.GetPixel(0, 0).A == 0 && result.GetPixel(3, 3).A == 0, "Global mode retained matching colors");

        string defaultInput = Path.Combine(directory, "background-default.png");
        File.Copy(sourcePath, defaultInput);
        Require(Run(["remove-background", defaultInput, "--mode", "edges"]).Code == 0, "Automatic background removal failed");
        string defaultOutput = Path.Combine(directory, "background-default_transparent.png");
        using (var result = new Bitmap(defaultOutput))
            Require(result.GetPixel(0, 0).A == 0 && result.GetPixel(3, 3).A == 255, "Default background options changed");

        Require(Run(["remove-background", sourcePath, "-o", edgeOutput]).Code == 1, "Existing transparent PNG accepted");
        Require(Run(["remove-background", sourcePath, "-o", edgeOutput, "--overwrite"]).Code == 0, "Background overwrite failed");
        Require(Run(["remove-background", "--help"]).Code == 0 && Run(["remove-bg", "--help"]).Code == 0,
            "Background command help failed");
    });

    Check("Background command rejects invalid parameters and reports file failures", () =>
    {
        string output = Path.Combine(directory, "invalid-background.png");
        string[][] invalid =
        [
            ["--mode", "all"], ["--background-color", "#12345"], ["--background-color", "#GGGGGG"],
            ["--tolerance", "-1"], ["--tolerance", "101"], ["--tolerance", "1.5"],
            ["--overwrite=true"], ["--json=true"], ["--unknown"], ["--sizes", "16"],
            ["-o", Path.Combine(directory, "invalid-background.jpg")]
        ];
        foreach (var extra in invalid)
        {
            var run = Run(["remove-background", input, "-o", output, .. extra]);
            Require(run.Code == 2 && run.Output.Length == 0 && run.Error.Length > 0,
                $"Expected background usage error for {string.Join(' ', extra)}; got {run.Code}: {run.Error}");
            Require(!File.Exists(output), "Invalid background arguments created output");
        }

        Require(Run(["remove-background"]).Code == 0, "Background command without arguments should show help");
        Require(Run(["remove-background", input, "-o", input, "--overwrite"]).Code == 2, "Background command overwrote source");
        Require(Run(["remove-background", Path.Combine(directory, "missing-background.png"), "-o", output]).Code == 1,
            "Missing background input");
        string corrupt = Path.Combine(directory, "bad-background-source.png");
        File.WriteAllText(corrupt, "not an image");
        Require(Run(["remove-background", corrupt, "-o", output]).Code == 1, "Corrupt background input");
    });
    Check("Independent resize and colors match the editor processors", () =>
    {
        string resizePath = Path.Combine(directory, "independent-resize.png");
        var resize = Run(["resize", input, "-o", resizePath, "--width", "13", "--height", "11",
            "--block-mode", "manual", "--brightness", "70", "--edge", "20", "--json"]);
        Require(resize.Code == 0 && resize.Error.Length == 0, resize.Error);
        using (var report = JsonDocument.Parse(resize.Output))
            Require(report.RootElement.GetProperty("process").GetString() == "resize-only", "Wrong resize report");
        using (var source = new Bitmap(input))
        using (var expected = IndependentImageProcessor.Scale(source, CliArguments.Parse([input,
            "--width", "13", "--height", "11", "--block-mode", "manual",
            "--brightness", "70", "--edge", "20"]).Options))
        using (var actual = new Bitmap(resizePath))
            EqualPixels(expected, actual);

        string colorsPath = Path.Combine(directory, "independent-colors.png");
        var colors = Run(["colors", input, "-o", colorsPath, "--palette", "step",
            "--palette-step", "51", "--quantization-colors", "8", "--color-weights", "--json"]);
        Require(colors.Code == 0 && colors.Error.Length == 0, colors.Error);
        using (var report = JsonDocument.Parse(colors.Output))
            Require(report.RootElement.GetProperty("process").GetString() == "colors-only", "Wrong colors report");
        using (var source = new Bitmap(input))
        using (var expected = IndependentImageProcessor.ApplyColors(source, CliArguments.Parse([input,
            "--palette", "step", "--palette-step", "51", "--quantization-colors", "8", "--color-weights"]).Options))
        using (var actual = new Bitmap(colorsPath))
        {
            EqualPixels(expected, actual);
            Require(actual.Size == source.Size, "Colors operation changed dimensions");
            for (int y = 0; y < source.Height; y++)
                for (int x = 0; x < source.Width; x++)
                    Require(actual.GetPixel(x, y).A == source.GetPixel(x, y).A, "Colors operation changed alpha");
        }

        string chained = Path.Combine(directory, "independent-chained.png");
        ExpectSuccess(["colors", resizePath, "-o", chained, "--palette", "none"]);
        using var result = new Bitmap(chained);
        Require(result.Size == new Size(13, 11), "Saved result cannot be used as input");
        Require(Run(["resize", "--help"]).Code == 0 && Run(["colors", "--help"]).Code == 0, "Independent help missing");
    });

    Check("Current editor image tools are exposed by CLI", () =>
    {
        string sourcePath = Path.Combine(directory, "current-tools.png");
        using (var fixture = new Bitmap(6, 4, PixelFormat.Format32bppArgb))
        {
            for (int y = 0; y < fixture.Height; y++)
                for (int x = 0; x < fixture.Width; x++)
                    fixture.SetPixel(x, y, x == 0 && y == 0 ? Color.Transparent :
                        (x + y) % 3 == 0 ? Color.FromArgb(180, 0, 0, 0) :
                        (x + y) % 3 == 1 ? Color.FromArgb(220, 255, 0, 0) : Color.FromArgb(255, 0, 255, 0));
            fixture.Save(sourcePath, ImageFormat.Png);
        }

        string monochromePath = Path.Combine(directory, "current-monochrome.png");
        ExpectSuccess(["monochrome", sourcePath, "-o", monochromePath, "--json"]);
        using (var source = new Bitmap(sourcePath))
        using (var expected = ImageFilters.Monochrome(source))
        using (var actual = new Bitmap(monochromePath))
            EqualPixels(expected, actual);

        string smoothPath = Path.Combine(directory, "current-smooth.png");
        ExpectSuccess(["smooth", sourcePath, "-o", smoothPath, "--passes", "2", "--color-tolerance", "18.5",
            "--strength", "70", "--detail-protection", "60", "--edge-protection", "50", "--json"]);
        using (var source = new Bitmap(sourcePath))
        using (var expected = IndependentImageProcessor.ApplyNeighborColors(source, new DownscaleOptions
        {
            LocalColorPasses = 2,
            Cleanup = new ColorCleanupOptions
            {
                ColorTolerance = 18.5, Strength = 0.7, DetailProtection = 0.6, EdgeProtection = 0.5
            }
        }))
        using (var actual = new Bitmap(smoothPath))
            EqualPixels(expected, actual);

        string replacePath = Path.Combine(directory, "current-replace.png");
        ExpectSuccess(["replace-color", sourcePath, "-o", replacePath, "--from", "#FF0000", "--to", "#123456", "--json"]);
        using (var replaced = new Bitmap(replacePath))
        {
            Require(replaced.GetPixel(1, 0).ToArgb() == Color.FromArgb(220, 0x12, 0x34, 0x56).ToArgb(), "Color replacement changed alpha or RGB");
            Require(replaced.GetPixel(0, 0).A == 0, "Color replacement changed transparent pixels");
        }

        var palette = Run(["palette-info", sourcePath, "--json"]);
        Require(palette.Code == 0 && palette.Error.Length == 0, palette.Error);
        using (var report = JsonDocument.Parse(palette.Output))
        {
            Require(report.RootElement.GetProperty("count").GetInt32() == 3, "Palette count differs from editor");
            Require(report.RootElement.GetProperty("colors").GetArrayLength() == 3, "Palette list missing");
        }

        string asciiText = Path.Combine(directory, "current-ascii.txt");
        string asciiImage = Path.Combine(directory, "current-ascii.png");
        ExpectSuccess(["ascii", sourcePath, "--text-output", asciiText, "--image-output", asciiImage,
            "--preset", "minimal", "--invert-source", "--invert-render", "--json"]);
        using (var source = new Bitmap(sourcePath))
            Require(File.ReadAllText(asciiText) == ImageFilters.ToAscii(source,
                ImageFilters.AsciiRampPresets[2].Characters, true), "ASCII text differs from editor processor");
        using (var source = new Bitmap(sourcePath))
        using (var expected = ImageFilters.RenderAscii(source, true, ImageFilters.AsciiRampPresets[2].Characters, true, true))
        using (var rendered = new Bitmap(asciiImage))
            EqualPixels(expected, rendered);
        string perPixelPath = Path.Combine(directory, "current-ascii-per-pixel.png");
        ExpectSuccess(["ascii", sourcePath, "-o", perPixelPath, "--mode", "per-pixel", "--preset", "blocks"]);
        using (var source = new Bitmap(sourcePath))
        using (var expected = ImageFilters.RenderAscii(source, false, ImageFilters.AsciiRampPresets[3].Characters))
        using (var rendered = new Bitmap(perPixelPath))
            EqualPixels(expected, rendered);

        foreach (string command in new[] { "monochrome", "mono", "smooth-colors", "smooth", "replace-color", "palette-info", "ascii" })
            Require(Run([command, "--help"]).Code == 0, $"Help missing for {command}");
        Require(Run(["replace-color", sourcePath, "--from", "red", "--to", "#FFFFFF"]).Code == 2,
            "Invalid replacement color accepted");
        Require(Run(["ascii", sourcePath, "--preset", "minimal", "--characters", "@."]).Code == 2,
            "Conflicting ASCII ramp accepted");
    });

    Check("Workflow executor matches the ordinary processors and owns its bitmap", () =>
    {
        string sourcePath = Path.Combine(directory, "workflow-source.png");
        using (var fixture = new Bitmap(12, 8, PixelFormat.Format32bppArgb))
        {
            for (int y = 0; y < fixture.Height; y++)
                for (int x = 0; x < fixture.Width; x++)
                    fixture.SetPixel(x, y, x < 4 ? Color.Black : x < 8 ? Color.FromArgb(255, 110, 120, 130) : Color.White);
            fixture.Save(sourcePath, ImageFormat.Png);
        }

        var resize = new DownscaleOptions { TargetWidth = 6, TargetHeight = 4 };
        var colors = new DownscaleOptions
        {
            IndependentColorMode = IndependentColorMode.Palette,
            Palette = PaletteKind.Step,
            PaletteStep = 51
        };
        var cleanup = new DownscaleOptions
        {
            LocalColorPasses = 2,
            Cleanup = new ColorCleanupOptions { Strength = 0, ColorTolerance = 12, DetailProtection = .8, EdgeProtection = .8 }
        };
        var recipe = new ProcessingRecipe
        {
            Steps =
            [
                new ProcessingStep { OperationId = ProcessingOperations.Resize, Downscale = resize },
                new ProcessingStep { OperationId = ProcessingOperations.Colors, Downscale = colors },
                new ProcessingStep { OperationId = ProcessingOperations.Cleanup, Downscale = cleanup },
                new ProcessingStep { OperationId = ProcessingOperations.Monochrome },
                new ProcessingStep
                {
                    OperationId = ProcessingOperations.ReplaceColor,
                    ColorReplacement = new ColorReplacementSettings
                    {
                        FromArgb = Color.Black.ToArgb(),
                        ToArgb = Color.Magenta.ToArgb()
                    }
                },
                new ProcessingStep
                {
                    OperationId = ProcessingOperations.RemoveBackground,
                    Background = new BackgroundSettings
                    {
                        Mode = BackgroundRemovalMode.GlobalColor,
                        Tolerance = 0,
                        ColorArgb = Color.White.ToArgb()
                    }
                }
            ]
        };
        var updates = new List<WorkflowProgress>();
        using var source = new Bitmap(sourcePath);
        int[] original = Enumerable.Range(0, source.Width * source.Height)
            .Select(q => source.GetPixel(q % source.Width, q / source.Width).ToArgb()).ToArray();
        using var actual = new WorkflowExecutor().Execute(source, recipe, progress: new InlineProgress<WorkflowProgress>(updates.Add));
        using var scaled = IndependentImageProcessor.Scale(source, resize);
        using var colored = IndependentImageProcessor.ApplyColors(scaled, colors);
        using var cleaned = IndependentImageProcessor.ApplyNeighborColors(colored, cleanup);
        using var monochrome = ImageFilters.Monochrome(cleaned);
        using var replaced = ImageColorTools.ReplaceColor(monochrome, Color.Black, Color.Magenta);
        using var expected = BackgroundRemover.Remove(replaced, BackgroundRemovalMode.GlobalColor, 0, Color.White);
        EqualPixels(expected, actual);
        Require(updates.Select(update => update.Index).SequenceEqual(Enumerable.Range(1, recipe.Steps.Count)),
            "Workflow progress is not one-based or skipped a completed step");
        Require(updates.All(update => update.Count == recipe.Steps.Count), "Workflow progress count changed");
        for (int q = 0; q < original.Length; q++)
            Require(source.GetPixel(q % source.Width, q / source.Width).ToArgb() == original[q], "Workflow changed its input");
    });

    Check("Workflow alignment state, validation and cancellation are deterministic", () =>
    {
        using var source = new Bitmap(8, 6, PixelFormat.Format32bppArgb);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                source.SetPixel(x, y, Color.FromArgb(255, x / 2 * 40, y / 2 * 60, (x / 2 + y / 2) * 30));
        var recipe = new ProcessingRecipe
        {
            Steps =
            [
                new ProcessingStep { OperationId = ProcessingOperations.Align },
                new ProcessingStep { OperationId = ProcessingOperations.Reduce, UsePreviousAlignment = true }
            ]
        };
        var completed = new List<WorkflowProgress>();
        using var actual = new WorkflowExecutor().Execute(source, recipe,
            stepCompleted: (update, image) =>
            {
                Require(image.Width > 0 && image.Height > 0, "Step callback received an invalid bitmap");
                completed.Add(update);
            });
        using var aligned = new PixelGridAligner().Align(source, new GridAlignmentOptions());
        using var expected = PixelGridReducer.Reduce(aligned.Aligned, aligned.CellSize);
        EqualPixels(expected, actual);
        Require(completed.Select(item => item.Index).SequenceEqual(new[] { 1, 2 }) &&
            completed.All(item => item.CellSize == aligned.CellSize),
            "Step callback lost order or resolved alignment cell size");

        var invalid = new ProcessingRecipe
        {
            Steps =
            [
                new ProcessingStep { OperationId = ProcessingOperations.Monochrome },
                new ProcessingStep { OperationId = "unknown.operation" }
            ]
        };
        var updates = new List<WorkflowProgress>();
        try
        {
            using var ignored = new WorkflowExecutor().Execute(source, invalid,
                progress: new InlineProgress<WorkflowProgress>(updates.Add));
            throw new InvalidOperationException("Unsupported workflow operation was accepted");
        }
        catch (WorkflowStepException ex)
        {
            Require(ex.StepIndex == 2 && ex.OperationId == "unknown.operation", "Workflow error lost step metadata");
            Require(updates.Count == 0, "Workflow started before the complete recipe was validated");
        }

        RequireThrows<InvalidDataException>(() => WorkflowExecutor.Validate(new ProcessingRecipe { Version = 2 }),
            "Unsupported workflow version was accepted");
        RequireThrows<InvalidDataException>(() => WorkflowExecutor.Validate(new ProcessingRecipe()),
            "Empty workflow was accepted");
        RequireWorkflowStep(1, ProcessingOperations.Resize, () => WorkflowExecutor.Validate(new ProcessingRecipe
        {
            Steps = [new ProcessingStep { OperationId = ProcessingOperations.Resize }]
        }));
        RequireWorkflowStep(1, ProcessingOperations.Reduce, () => WorkflowExecutor.Validate(new ProcessingRecipe
        {
            Steps = [new ProcessingStep { OperationId = ProcessingOperations.Reduce, UsePreviousAlignment = true }]
        }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            using var ignored = new WorkflowExecutor().Execute(source, recipe, cancellation.Token);
            throw new InvalidOperationException("Cancelled workflow executed");
        }
        catch (WorkflowCanceledException ex)
        {
            Require(ex.StepIndex == 1 && ex.OperationId == ProcessingOperations.Align,
                "Cancellation before a step lost its metadata");
        }

        using var betweenSteps = new CancellationTokenSource();
        var twoSteps = new ProcessingRecipe
        {
            Steps =
            [
                new ProcessingStep { OperationId = ProcessingOperations.Monochrome },
                new ProcessingStep { OperationId = ProcessingOperations.Ascii, Ascii = new AsciiSettings() }
            ]
        };
        try
        {
            using var ignored = new WorkflowExecutor().Execute(source, twoSteps, betweenSteps.Token,
                new InlineProgress<WorkflowProgress>(update =>
                {
                    if (update.Index == 1) betweenSteps.Cancel();
                }));
            throw new InvalidOperationException("Workflow continued after cancellation between steps");
        }
        catch (WorkflowCanceledException ex)
        {
            Require(ex.StepIndex == 2 && ex.OperationId == ProcessingOperations.Ascii,
                "Cancellation between steps lost the next step metadata");
        }
    });

    Check("Workflow profile and ASCII steps match their ordinary processors", () =>
    {
        using var source = new Bitmap(input);
        var options = new DownscaleOptions
        {
            TargetWidth = 11,
            TargetHeight = 9,
            Palette = PaletteKind.DB16,
            Quantization = QuantizationMethod.MedianCut,
            QuantizationColors = 12,
            ThreadCount = 1
        };
        using var actualProfile = new WorkflowExecutor().Execute(source, new ProcessingRecipe
        {
            Steps = [new ProcessingStep { OperationId = ProcessingOperations.Profile, Downscale = options }]
        });
        var processed = new PixelArtDownscaler().Process(source, WorkflowJson.Snapshot(options));
        using var crop = processed.CroppedSource;
        using var expectedProfile = processed.Downscaled;
        EqualPixels(expectedProfile, actualProfile);

        var ascii = new AsciiSettings
        {
            Characters = ImageFilters.AsciiRampPresets[2].Characters,
            KeepSourceSize = false,
            InvertSource = true,
            InvertColors = true
        };
        using var actualAscii = new WorkflowExecutor().Execute(source, new ProcessingRecipe
        {
            Steps = [new ProcessingStep { OperationId = ProcessingOperations.Ascii, Ascii = ascii }]
        });
        using var expectedAscii = ImageFilters.RenderAscii(source, ascii.KeepSourceSize, ascii.Characters,
            ascii.InvertSource, ascii.InvertColors);
        EqualPixels(expectedAscii, actualAscii);
    });

    Check("Macro CLI lists and applies the same workflow executor", () =>
    {
        string library = Path.Combine(directory, "macro-library");
        var store = new MacroStore(library);
        MacroDefinition saved = store.Save(new MacroDefinition
        {
            Name = "Small monochrome",
            Steps =
            [
                new ProcessingStep
                {
                    OperationId = ProcessingOperations.Resize,
                    Downscale = new DownscaleOptions { TargetWidth = 9, TargetHeight = 7, ThreadCount = 123 }
                },
                new ProcessingStep { OperationId = ProcessingOperations.Monochrome }
            ]
        });
        string macroPath = store.PathFor(saved.Id);
        Require(!File.ReadAllText(macroPath).Contains("threadCount", StringComparison.OrdinalIgnoreCase),
            "Macro persisted a machine-local thread count");
        Require(saved.Steps[0].Downscale!.ThreadCount == Environment.ProcessorCount,
            "Saved macro did not restore a local thread count");
        string outputPath = Path.Combine(directory, "macro-output.png");
        var list = Run(["macro", "list", "--library", library, "--json"]);
        Require(list.Code == 0 && list.Error.Length == 0, list.Error);
        using (var report = JsonDocument.Parse(list.Output))
        {
            Require(report.RootElement.GetProperty("count").GetInt32() == 1, "Macro list count differs");
            Require(report.RootElement.GetProperty("macros")[0].GetProperty("name").GetString() == saved.Name,
                "Macro list lost its name");
        }

        var apply = Run(["macro", "apply", input, "--macro", macroPath, "-o", outputPath, "--json"]);
        Require(apply.Code == 0 && apply.Error.Length == 0, apply.Error);
        using (var source = new Bitmap(input))
        using (var expected = new WorkflowExecutor().Execute(source,
            new ProcessingRecipe { Version = saved.Version, Steps = saved.Steps }))
        using (var actual = new Bitmap(outputPath))
            EqualPixels(expected, actual);
        Require(Run(["macro", "apply", input, "--macro", macroPath, "-o", outputPath]).Code == 1,
            "Macro apply overwrote an output without --overwrite");
        Require(Run(["macro", "apply", input, "--macro", macroPath, "-o", outputPath, "--overwrite"]).Code == 0,
            "Macro apply did not honor --overwrite");
        Require(Run(["macro", "--help"]).Code == 0 && Run(["macro", "list", "--help"]).Code == 0 &&
            Run(["macro", "apply", "--help"]).Code == 0, "Macro help is incomplete");
        Require(Run(["macro", "unknown"]).Code == 2, "Unknown macro command was accepted");
        Require(Run(["macro", "apply", input, "--macro", macroPath]).Code == 2, "Macro output was optional");
        Require(Run(["macro", "apply", input, "--macro", Path.Combine(directory, "missing-macro.json"),
            "-o", Path.Combine(directory, "missing-macro-output.png")]).Code == 1, "Missing macro was not a file error");

        string invalidMacro = Path.Combine(store.DirectoryPath, "invalid.json");
        File.WriteAllText(invalidMacro, """{"version":2,"id":"00000000-0000-0000-0000-000000000001","name":"Old","steps":[{"operationId":"filter.monochrome"}]}""");
        Require(Run(["macro", "apply", input, "--macro", invalidMacro,
            "-o", Path.Combine(directory, "invalid-macro-output.png")]).Code == 1, "Unsupported macro version was accepted");
    });

    Check("RGBA channel macros preserve PNG channels and support value/percent add/subtract", () =>
    {
        string sourcePath = Path.Combine(directory, "channels-source.png");
        using var source = new Bitmap(3, 1, PixelFormat.Format32bppArgb);
        source.SetPixel(0, 0, Color.FromArgb(0, 12, 34, 56));
        source.SetPixel(1, 0, Color.FromArgb(97, 100, 150, 200));
        source.SetPixel(2, 0, Color.FromArgb(255, 255, 0, 2));
        source.Save(sourcePath, ImageFormat.Png);
        byte[] original = File.ReadAllBytes(sourcePath);
        var store = new MacroStore(Path.Combine(directory, "channels-library"));
        foreach (var channel in Enum.GetValues<RgbaChannel>())
        foreach (var mode in Enum.GetValues<ChannelAdjustmentMode>())
        foreach (bool subtract in new[] { false, true })
        {
            var options = new ChannelAdjustmentOptions { Channel = channel, Mode = mode,
                Amount = mode == ChannelAdjustmentMode.Value ? 12.5 : 25.5, Subtract = subtract };
            string label = $"{channel}-{mode}-{subtract}";
            var saved = store.Save(new MacroDefinition { Name = label,
                Steps = [new ProcessingStep { OperationId = ProcessingOperations.AdjustChannel, ChannelAdjustment = options }] });
            string outputPath = Path.Combine(directory, "channels-" + label + ".png");
            var apply = Run(["macro", "apply", sourcePath, "--macro", store.PathFor(saved.Id), "-o", outputPath, "--json"]);
            Require(apply.Code == 0 && apply.Error.Length == 0, apply.Error);
            using var expected = ChannelAdjustment.Apply(source, options);
            using var actual = new Bitmap(outputPath);
            EqualPixels(expected, actual);
        }
        Require(File.ReadAllBytes(sourcePath).SequenceEqual(original), "RGBA macro modified its source");
    });

    Check("Trim macro CLI keeps semitransparent boundaries and source PNG", () =>
    {
        string sourcePath = Path.Combine(directory, "trim-source.png");
        string outputPath = Path.Combine(directory, "trim-output.png");
        using (var source = new Bitmap(8, 7, PixelFormat.Format32bppArgb))
        {
            source.SetPixel(1, 2, Color.FromArgb(1, 12, 34, 56));
            source.SetPixel(5, 6, Color.FromArgb(128, 210, 70, 30));
            source.Save(sourcePath, ImageFormat.Png);
        }
        byte[] original = File.ReadAllBytes(sourcePath);
        var store = new MacroStore(Path.Combine(directory, "trim-library"));
        var saved = store.Save(new MacroDefinition
        {
            Name = "Transparent trim",
            Steps = [new ProcessingStep { OperationId = ProcessingOperations.Trim }]
        });
        var apply = Run(["macro", "apply", sourcePath, "--macro", store.PathFor(saved.Id), "-o", outputPath, "--json"]);
        Require(apply.Code == 0 && apply.Error.Length == 0, apply.Error);
        using var expected = new Bitmap(5, 5, PixelFormat.Format32bppArgb);
        expected.SetPixel(0, 0, Color.FromArgb(1, 12, 34, 56));
        expected.SetPixel(4, 4, Color.FromArgb(128, 210, 70, 30));
        using var actual = new Bitmap(outputPath);
        EqualPixels(expected, actual);
        Require(File.ReadAllBytes(sourcePath).SequenceEqual(original), "Trim macro overwrote its source");
    });

    Check("Contour cleanup and heatmap replay through saved macros", () =>
    {
        string sourcePath = Path.Combine(directory, "contour-macro-source.png");
        using var source = new Bitmap(9, 9, PixelFormat.Format32bppArgb);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                source.SetPixel(x, y, Color.FromArgb(255, x < 4 ? 20 : 230, x < 4 ? 30 : 220, x < 4 ? 40 : 210));
        source.SetPixel(3, 4, Color.Magenta);
        source.Save(sourcePath, ImageFormat.Png);
        byte[] original = File.ReadAllBytes(sourcePath);
        var options = new ContourCleanOptions { WindowSize = 5, Strictness = ContourCleanStrictness.Strong,
            SimilarColorDistance = 20, MinimumAgreement = 0.4, MinimumOpposition = 0.2 };
        var store = new MacroStore(Path.Combine(directory, "contour-macro-library"));
        var saved = store.Save(new MacroDefinition { Name = "Contour map after cleanup", Steps =
        [
            new ProcessingStep { OperationId = ProcessingOperations.CleanContours, Contours = options },
            new ProcessingStep { OperationId = ProcessingOperations.ContourHeatmap, Contours = options }
        ] });
        string outputPath = Path.Combine(directory, "contour-macro-output.png");
        var apply = Run(["macro", "apply", sourcePath, "--macro", store.PathFor(saved.Id), "-o", outputPath]);
        Require(apply.Code == 0 && apply.Error.Length == 0, apply.Error);
        var cleaned = ContourNoiseCleaner.Process(source, options);
        using var cleanedImage = cleaned.Image;
        using var candidates = cleaned.CandidateMap;
        using var expected = ContourNoiseCleaner.Analyze(cleaned.Image, options).Heatmap;
        using var actual = new Bitmap(outputPath);
        EqualPixels(expected, actual);
        Require(File.ReadAllBytes(sourcePath).SequenceEqual(original), "Contour macro modified its source");

        RequireWorkflowStep(1, ProcessingOperations.ContourHeatmap, () => WorkflowExecutor.Validate(new ProcessingRecipe
        {
            Steps = [new ProcessingStep { OperationId = ProcessingOperations.ContourHeatmap }]
        }));
        RequireWorkflowStep(1, ProcessingOperations.CleanContours, () => WorkflowExecutor.Validate(new ProcessingRecipe
        {
            Steps = [new ProcessingStep { OperationId = ProcessingOperations.CleanContours,
                Contours = new ContourCleanOptions { WindowSize = 4 } }]
        }));
    });

    Check("Color cleanup controls merge close shades and preserve pixels at zero strength", () =>
    {
        string sourcePath = Path.Combine(directory, "cleanup-controls.png");
        using (var fixture = new Bitmap(9, 9, PixelFormat.Format32bppArgb))
        {
            using (var graphics = Graphics.FromImage(fixture)) graphics.Clear(Color.FromArgb(100, 100, 100));
            fixture.SetPixel(4, 4, Color.FromArgb(128, 104, 104, 104));
            fixture.Save(sourcePath, ImageFormat.Png);
        }
        string cleanedPath = Path.Combine(directory, "cleanup-controls-result.png");
        ExpectSuccess(["smooth", sourcePath, "-o", cleanedPath, "--strength", "100", "--detail-protection", "0", "--edge-protection", "0"]);
        using (var cleaned = new Bitmap(cleanedPath))
            Require(cleaned.GetPixel(4, 4).ToArgb() == Color.FromArgb(128, 100, 100, 100).ToArgb(), "Cleanup controls did not merge the shade while retaining alpha");
        string unchangedPath = Path.Combine(directory, "cleanup-controls-zero.png");
        ExpectSuccess(["smooth", sourcePath, "-o", unchangedPath, "--strength", "0"]);
        using (var source = new Bitmap(sourcePath))
        using (var unchanged = new Bitmap(unchangedPath)) EqualPixels(source, unchanged);
        foreach (string value in new[] { "NaN", "Infinity", "-1", "41" })
            Require(Run(["smooth", sourcePath, "--color-tolerance", value]).Code == 2, "Invalid tolerance accepted: " + value);
        Require(Run(["smooth", sourcePath, "--strength", "101"]).Code == 2, "Invalid cleanup strength accepted");
    });

    Check("Colors command exposes current quantization and palette modes", () =>
    {
        string quantizedPath = Path.Combine(directory, "mode-quantization.png");
        ExpectSuccess(["colors", input, "-o", quantizedPath, "--color-mode", "quantization",
            "--quantization", "median-cut", "--quantization-colors", "8"]);
        using (var source = new Bitmap(input))
        using (var expected = IndependentImageProcessor.ApplyColors(source, new DownscaleOptions
        {
            IndependentColorMode = IndependentColorMode.Quantization,
            Quantization = QuantizationMethod.MedianCut,
            QuantizationColors = 8
        }))
        using (var actual = new Bitmap(quantizedPath))
            EqualPixels(expected, actual);

        string palettePath = Path.Combine(directory, "mode-palette.png");
        ExpectSuccess(["colors", input, "-o", palettePath, "--color-mode", "palette",
            "--palette", "step", "--palette-step", "32"]);
        using (var source = new Bitmap(input))
        using (var expected = IndependentImageProcessor.ApplyColors(source, new DownscaleOptions
        {
            IndependentColorMode = IndependentColorMode.Palette,
            Palette = PaletteKind.Step,
            PaletteStep = 32
        }))
        using (var actual = new Bitmap(palettePath))
            EqualPixels(expected, actual);

        Require(Run(["colors", input, "--color-mode", "quantization", "--palette", "db16"]).Code == 2,
            "Quantization-only mode accepted palette settings");
        Require(Run(["colors", input, "--color-mode", "palette", "--quantization-colors", "8"]).Code == 2,
            "Palette-only mode accepted quantization settings");
        Require(Run([input, "--color-mode", "palette"]).Code == 2,
            "Combined downscale accepted independent color mode");
    });

    Check("Resize command enlarges without changing source pixels or alpha", () =>
    {
        string enlargedPath = Path.Combine(directory, "independent-upscale.png");
        var run = Run(["resize", input, "-o", enlargedPath, "--width", "134", "--height", "122"]);
        Require(run.Code == 0 && run.Error.Length == 0, run.Error);
        using var source = new Bitmap(input);
        using var enlarged = new Bitmap(enlargedPath);
        Require(enlarged.Size == new Size(134, 122), "Resize did not enlarge the image");
        for (int y = 0; y < enlarged.Height; y++)
            for (int x = 0; x < enlarged.Width; x++)
                Require(enlarged.GetPixel(x, y).ToArgb() == source.GetPixel(x / 2, y / 2).ToArgb(),
                    "Upscale changed a source color or alpha");
    });
    Check("Independent commands reject unrelated settings and protect transparent outputs", () =>
    {
        string target = Path.Combine(directory, "independent-invalid.png");
        Require(Run(["resize", input, "-o", target, "--palette", "db16"]).Code == 2, "Resize accepted palette");
        Require(Run(["colors", input, "-o", target, "--width", "12"]).Code == 2, "Colors accepted width");
        Require(Run(["colors", input, "-o", target, "--sprite"]).Code == 2, "Colors accepted sprite mode");
        Require(Run(["resize", input, "-o", Path.Combine(directory, "lossy.jpg")]).Code == 2, "Resize accepted JPEG");
        Require(Run(["colors", input, "-o", input, "--overwrite"]).Code == 2, "Colors overwrote source");
        Require(!File.Exists(target), "Invalid independent command wrote output");
    });

    Check("Aseprite command exports a batch and reports individual failures", () =>
    {
        string source = Path.Combine(directory, "sample.aseprite");
        File.WriteAllBytes(source, MinimalAseprite());
        string destination = Path.Combine(directory, "aseprite-export");
        string missing = Path.Combine(directory, "missing.ase");
        var run = Run(["aseprite", source, missing, "--output-dir", destination, "--layout", "grid",
            "--columns", "2", "--padding", "1", "--inspection", "--json"]);
        Require(run.Code == 1 && run.Error.Length == 0, "Batch did not report partial failure through JSON");
        using (var report = JsonDocument.Parse(run.Output))
        {
            Require(report.RootElement.GetProperty("succeeded").GetInt32() == 1, "Batch success count: " + run.Output);
            Require(report.RootElement.GetProperty("failed").GetInt32() == 1, "Batch failure count");
        }
        Require(File.Exists(Path.Combine(destination, "sample.png")) &&
                File.Exists(Path.Combine(destination, "sample.json")) &&
                File.Exists(Path.Combine(destination, "sample.inspection.json")), "Aseprite outputs missing");
        using (var bitmap = new Bitmap(Path.Combine(destination, "sample.png")))
        using (var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(destination, "sample.json"))))
        {
            Require(bitmap.Size == new Size(2, 2), "Aseprite sheet size");
            Require(metadata.RootElement.GetProperty("schema").GetString() == "aseprite-offline/v1", "Aseprite JSON schema");
            Require(metadata.RootElement.GetProperty("frameCount").GetInt32() == 1, "Aseprite frame count");
        }
        Require(Run(["aseprite", source, "--output-dir", destination]).Code == 1, "Existing Aseprite output overwritten");
        Require(Run(["aseprite", source, "--columns", "2"]).Code == 2, "Columns accepted outside grid");
        Require(Run(["aseprite", source, "--layout", "wrong"]).Code == 2, "Invalid layout accepted");
        Require(Run(["aseprite", "--help"]).Code == 0, "Aseprite help missing");
    });
    Check("Alignment is an independent full-size operation with no 64-color quantization", () =>
    {
        string sourcePath = Path.Combine(directory, "alignment-source.png");
        string outputPath = Path.Combine(directory, "aligned.png");
        using var source = new Bitmap(40, 32, PixelFormat.Format32bppArgb);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                source.SetPixel(x, y, Color.FromArgb((x / 4 + y / 4) % 5 == 0 ? 97 : 255,
                    x / 4 * 23, y / 4 * 29, (x / 4 * 17 + y / 4 * 11) % 256));
        source.Save(sourcePath, ImageFormat.Png);
        var run = Run(["align", sourcePath, "-o", outputPath, "--cell-size", "4", "--json"]);
        Require(run.Code == 0 && run.Error.Length == 0, run.Error);
        using var actual = new Bitmap(outputPath);
        EqualPixels(source, actual);
        using var report = JsonDocument.Parse(run.Output);
        Require(report.RootElement.GetProperty("process").GetString() == "grid-alignment", "Wrong process");
        Require(report.RootElement.GetProperty("width").GetInt32() == 40, "Unexpected downscale");
        Require(Run(["align", sourcePath, "-o", sourcePath]).Code == 2, "Original overwrite allowed");
        Require(Run(["align", sourcePath, "-o", outputPath]).Code == 1, "Output overwrite allowed");
        Require(Run(["align", sourcePath, "--palette", "none"]).Code == 2, "Downscale option accepted");
        Require(Run(["align", sourcePath, "--cell-size", "1"]).Code == 2, "Invalid pitch accepted");
        Require(Run(["align", sourcePath, "-o", Path.Combine(directory, "aligned.jpg")]).Code == 2, "Lossy output allowed");
        Require(Run(["align", "--help"]).Code == 0, "Alignment help failed");
    });

    Check("Local snapping CLI is opt-in, reports shifts and permits exact compaction", () =>
    {
        string sourcePath = Path.Combine(directory, "local-snap.png");
        string offPath = Path.Combine(directory, "local-snap-off.png"), onPath = Path.Combine(directory, "local-snap-on.png");
        using (var source = new Bitmap(60, 40, PixelFormat.Format32bppArgb))
        {
            using var graphics = Graphics.FromImage(source); graphics.Clear(Color.FromArgb(20, 20, 30));
            using var brush = new SolidBrush(Color.White);
            graphics.FillRectangle(brush, 12, 12, 5, 10); graphics.FillRectangle(brush, 17, 17, 5, 5);
            source.Save(sourcePath, ImageFormat.Png);
        }
        var off = Run(["align", sourcePath, "--mode", "local", "--cell-size", "5", "--json", "-o", offPath]);
        Require(off.Code == 0, off.Error);
        using (var report = JsonDocument.Parse(off.Output))
            Require(!report.RootElement.GetProperty("snapToGlobalGrid").GetBoolean(), "Snapping default changed");
        using (var source = new Bitmap(sourcePath)) using (var actual = new Bitmap(offPath)) EqualPixels(source, actual);
        var on = Run(["align", sourcePath, "--mode", "local", "--cell-size", "5", "--snap-to-grid", "--json", "-o", onPath]);
        Require(on.Code == 0, on.Error);
        using (var report = JsonDocument.Parse(on.Output))
            Require(report.RootElement.GetProperty("snapToGlobalGrid").GetBoolean()
                && report.RootElement.GetProperty("shiftedRegionCount").GetInt32() == 1, "Snap metadata missing");
        using (var actual = new Bitmap(onPath))
        using (var compact = PixelGridReducer.Reduce(actual, 5))
            Require(compact.Width == 12 && compact.GetPixel(2, 2).R == 255 && compact.GetPixel(3, 3).R == 255, "Snapped shape changed");
        Require(Run(["align", sourcePath, "--snap-to-grid"]).Code == 2, "Global mode accepted a local snap flag");
        Require(Run(["align", sourcePath, "--mode", "local", "--snap-to-grid=false"]).Code == 2, "Flag accepted an ambiguous value");
    });

    Check("Grid reduction copies exact cells, including partial edges and alpha", () =>
    {
        string aligned = Path.Combine(directory, "reduction-aligned.png");
        string target = Path.Combine(directory, "reduction-aligned_pixels.png");
        using (var source = new Bitmap(9, 7, PixelFormat.Format32bppArgb))
        {
            for (int y = 0; y < source.Height; y++)
                for (int x = 0; x < source.Width; x++)
                    source.SetPixel(x, y, Color.FromArgb((x / 4 + y / 4) % 2 == 0 ? 125 : 255,
                        x / 4 * 75, y / 4 * 90, (x / 4 + y / 4) * 25));
            source.Save(aligned, ImageFormat.Png);
        }
        var run = Run(["reduce-grid", aligned, "--cell-size", "4", "--json"]);
        Require(run.Code == 0 && run.Error.Length == 0, run.Error);
        using (var report = JsonDocument.Parse(run.Output))
        {
            Require(report.RootElement.GetProperty("process").GetString() == "grid-reduction", "Wrong reduction report");
            Require(report.RootElement.GetProperty("cellSize").GetInt32() == 4, "Wrong reduction pitch");
            Require(report.RootElement.GetProperty("outputSize").GetProperty("width").GetInt32() == 3, "Wrong reduced width");
        }
        using (var source = new Bitmap(aligned))
        using (var expected = PixelArtAlignment.PixelGridReducer.Reduce(source, 4))
        using (var actual = new Bitmap(target))
            EqualPixels(expected, actual);
        Require(Run(["compact-grid", aligned, "--cell-size", "4", "-o", target]).Code == 1, "Existing reduced file accepted");
        Require(Run(["compact-grid", aligned, "--cell-size", "4", "-o", target, "--overwrite"]).Code == 0,
            "Reduction overwrite failed");

        string irregular = Path.Combine(directory, "reduction-irregular.png");
        using (var source = new Bitmap(aligned))
        {
            source.SetPixel(1, 1, Color.Magenta);
            source.Save(irregular, ImageFormat.Png);
        }
        var irregularRun = Run(["reduce-grid", irregular, "--cell-size", "4"]);
        Require(irregularRun.Code == 1, $"Nonuniform grid accepted: {irregularRun.Code} {irregularRun.Error}");
        Require(!File.Exists(Path.Combine(directory, "reduction-irregular_pixels.png")), "Failed reduction created output");
    });

    Check("Grid reduction validates arguments and protects the source", () =>
    {
        string aligned = Path.Combine(directory, "reduction-aligned.png");
        Require(Run(["reduce-grid", aligned]).Code == 2, "Missing cell size accepted");
        Require(Run(["reduce-grid", aligned, "--cell-size", "1"]).Code == 2, "Cell size one accepted");
        var oversizedRun = Run(["reduce-grid", aligned, "--cell-size", "100"]);
        Require(oversizedRun.Code == 2, $"Oversized cell accepted: {oversizedRun.Code} {oversizedRun.Error}");
        Require(Run(["reduce-grid", aligned, "--cell-size", "4", "-o", aligned, "--overwrite"]).Code == 2,
            "Reduction overwrote source");
        Require(Run(["reduce-grid", aligned, "--cell-size", "4", "-o", Path.Combine(directory, "lossy.jpg")]).Code == 2,
            "Lossy reduction output accepted");
        Require(Run(["reduce-grid", "--help"]).Code == 0 && Run(["compact-grid", "--help"]).Code == 0,
            "Reduction help missing");
    });
    Check("Pixel-art finish creates a compact PNG and protects the input", () =>
    {
        string target = Path.Combine(directory, "finished.png");
        var run = Run(["finish-pixel-art", input, "-o", target, "--cell-size", "8", "--colors", "8", "--json"]);
        Require(run.Code == 0 && run.Error.Length == 0, run.Error);
        using var report = JsonDocument.Parse(run.Output);
        Require(report.RootElement.GetProperty("process").GetString() == "finish-pixel-art", "Wrong finish report");
        Require(report.RootElement.GetProperty("outputSize").GetProperty("width").GetInt32() == 9, "Wrong finish width");
        using var actual = new Bitmap(target);
        Require(actual.Size == new Size(9, 8), "Wrong compact dimensions");
        Require(Run(["finish-pixel-art", input, "-o", target]).Code == 1, "Existing finish result accepted");
        Require(Run(["finish-pixel-art", input, "-o", input, "--overwrite"]).Code == 2, "Finish overwrote source");
        Require(Run(["finish-pixel-art", input, "--cell-size", "1"]).Code == 2, "Invalid finish cell accepted");
        Require(Run(["finish-pixel-art", "--help"]).Code == 0, "Finish help missing");
    });
    Check("Sprite mode keeps odd-sized canvas edges, transparency and preview pixels", () =>
    {
        string sprite = Path.Combine(directory, "sprite.png");
        string output = Path.Combine(directory, "sprite-result.png");
        string preview = Path.Combine(directory, "sprite-preview.png");
        string crop = Path.Combine(directory, "sprite-source.png");
        using (var fixture = new Bitmap(7, 5, PixelFormat.Format32bppArgb))
        {
            fixture.SetPixel(6, 4, Color.White);
            fixture.Save(sprite, ImageFormat.Png);
        }
        ExpectSuccess([sprite, "-o", output, "--sprite", "--alpha-threshold", "10", "--palette", "none",
            "--width", "3", "--height", "2", "--preview-output", preview, "--preview-scale", "2", "--cropped-output", crop]);
        using var actual = new Bitmap(output);
        using var original = new Bitmap(crop);
        using var enlarged = new Bitmap(preview);
        Require(actual.Size == new Size(3, 2) && original.Size == new Size(7, 5), "Canvas was cropped");
        Require(actual.GetPixel(0, 0).A == 0, "Transparent background lost");
        Require(actual.GetPixel(2, 1).ToArgb() == Color.White.ToArgb(), "Bottom/right pixel lost or darkened");
        for (int y = 0; y < enlarged.Height; y++)
            for (int x = 0; x < enlarged.Width; x++)
                Require(enlarged.GetPixel(x, y).ToArgb() == actual.GetPixel(x / 2, y / 2).ToArgb(), "Preview changed alpha/color");
        Require(Run([sprite, "-o", Path.Combine(directory, "sprite.jpg"), "--sprite"]).Code == 2, "Lossy alpha export allowed");
        Require(Run([sprite, "--alpha-threshold", "50"]).Code == 2, "Orphan threshold accepted");
        Require(Run([sprite, "--sprite", "--crop-horizontal", "left"]).Code == 2, "Sprite crop accepted");
        Require(Run([sprite, "--sprite", "--dithering"]).Code == 2, "Sprite dithering accepted");
    });

    Check("Sprite quantization ignores hidden RGB and alpha thresholds change the silhouette", () =>
    {
        using var source = new Bitmap(24, 24, PixelFormat.Format32bppArgb);
        using var hidden = new Bitmap(24, 24, PixelFormat.Format32bppArgb);
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 24; x++)
            {
                var color = x % 2 == 0 ? Color.FromArgb(255, x * 10, y * 10, 180) : Color.Transparent;
                source.SetPixel(x, y, color);
                hidden.SetPixel(x, y, x % 2 == 0 ? color : Color.FromArgb(0, x * 10, y * 10, 200));
            }
        foreach (var method in Enum.GetValues<QuantizationMethod>())
        foreach (bool weighted in new[] { false, true })
        {
            var options = new DownscaleOptions { SpriteMode = true, TargetWidth = 12, TargetHeight = 12,
                Palette = PaletteKind.None, Quantization = method, QuantizationColors = 8,
                UseColorWeights = weighted, AlphaThreshold = 25, ThreadCount = 1 };
            var first = new PixelArtDownscaler().Process(source, options);
            var second = new PixelArtDownscaler().Process(hidden, options);
            using var a = first.Downscaled; using var b = second.Downscaled;
            using var ac = first.CroppedSource; using var bc = second.CroppedSource;
            EqualPixels(a, b);
            Require(a.GetPixel(0, 0).A == 255 && a.GetPixel(0, 0).B == 180, "Transparent RGB leaked into color");
        }
        var thin = new PixelArtDownscaler().Process(source, new DownscaleOptions { SpriteMode = true,
            TargetWidth = 12, TargetHeight = 12, Palette = PaletteKind.None, AlphaThreshold = 75 });
        using var thinOutput = thin.Downscaled; using var thinCrop = thin.CroppedSource;
        Require(thinOutput.GetPixel(0, 0).A == 0, "Coverage threshold ignored");
        using var empty = new Bitmap(5, 3, PixelFormat.Format32bppArgb);
        var blank = new PixelArtDownscaler().Process(empty, new DownscaleOptions { SpriteMode = true, TargetWidth = 2, TargetHeight = 1 });
        using var blankOutput = blank.Downscaled; using var blankCrop = blank.CroppedSource;
        Require(blankOutput.GetPixel(0, 0).A == 0 && blank.DominantColor.A == 0, "Empty sprite failed");
    });

    Check("Default CLI options match the library", () =>
    {
        var options = CliArguments.Parse([input]).Options;
        Require(options.TargetWidth == 48 && options.TargetHeight == 48, "Default size");
        var largeSize = CliArguments.Parse([input, "--width", "7680", "--height", "4320"]).Options;
        Require(largeSize.TargetWidth == 7680 && largeSize.TargetHeight == 4320,
            "CLI resize retained the 4K dimension cap");
        Require(options.Palette == PaletteKind.DB16 && options.PaletteStep == 32, "Default palette");
        Require(options.Quantization == QuantizationMethod.KMeansLab && options.QuantizationColors == 64, "Default quantization");
        Require(!options.UseColorWeights, "Color weights must be opt-in");
        Require(options.ThreadCount == Environment.ProcessorCount, "Default threads");
        Require(options.CropHorizontal == CropHorizontalAlignment.Center && options.CropVertical == CropVerticalAlignment.Center, "Default crop");
        Require(options.BlockMode == BlockSelectionMode.Automatic && options.ManualCriteria is null && !options.EnableDithering, "Default mode");
        string path = Path.Combine(directory, "исходное изображение_downscaled.png");
        ExpectSuccess([input, "--json"]);
        CompareWithLibrary(input, path, new DownscaleOptions());
    });

    Check("Color frequency changes the representative in every quantization method", () =>
    {
        using var source = new Bitmap(100, 1, PixelFormat.Format24bppRgb);
        for (int x = 0; x < source.Width; x++)
        {
            int gray = x == 0 ? 120 : x == 1 ? 200 : 40;
            source.SetPixel(x, 0, Color.FromArgb(gray, gray, gray));
        }
        foreach (var method in Enum.GetValues<QuantizationMethod>())
        {
            using var unweighted = ColorQuantizer.Quantize(source, 1, method, 1, false);
            using var weighted = ColorQuantizer.Quantize(source, 1, method, 1, true);
            Require(weighted.GetPixel(0, 0).R == 40, $"{method}: frequent color has no influence");
            Require(unweighted.GetPixel(0, 0).R != 40, $"{method}: disabled weights still affect palette");
            Require(DistinctColors(weighted) == 1 && DistinctColors(unweighted) == 1, "One-color budget ignored");
        }
    });

    Check("Weighted Median Cut partitions by population as well as averaging by it", () =>
    {
        using var source = new Bitmap(103, 1, PixelFormat.Format24bppRgb);
        for (int x = 0; x < source.Width; x++)
        {
            int gray = x < 100 ? 0 : (x - 99) * 80;
            source.SetPixel(x, 0, Color.FromArgb(gray, gray, gray));
        }
        using var weighted = ColorQuantizer.Quantize(source, 2, QuantizationMethod.MedianCut, 1, true);
        Require(weighted.GetPixel(0, 0).R == 0, "Common black color lost");
        Require(weighted.GetPixel(100, 0).ToArgb() == weighted.GetPixel(102, 0).ToArgb(), "Rare colors were not grouped together");
        Require(DistinctColors(weighted) == 2, "Weighted split created an empty bucket");
    });

    Check("Custom color budgets work below and above 64 with weights on and off", () =>
    {
        using var source = new Bitmap(128, 1, PixelFormat.Format24bppRgb);
        for (int x = 0; x < source.Width; x++)
            source.SetPixel(x, 0, Color.FromArgb(x * 2, 100, 180));
        foreach (var method in Enum.GetValues<QuantizationMethod>())
        foreach (bool weighted in new[] { false, true })
        {
            using var reduced = ColorQuantizer.Quantize(source, 8, method, 1, weighted);
            Require(DistinctColors(reduced) <= 8, $"{method}: color budget exceeded");
            using var larger = ColorQuantizer.Quantize(source, 128, method, 1, weighted);
            EqualPixels(source, larger);
            using var maximum = ColorQuantizer.Quantize(source, DownscaleOptions.MaxQuantizationColors, method, 1, weighted);
            EqualPixels(source, maximum);
        }
        foreach (int invalid in new[] { 0, DownscaleOptions.MaxQuantizationColors + 1 })
        {
            bool rejected = false;
            try { using var output = ColorQuantizer.Quantize(source, invalid, QuantizationMethod.MedianCut); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "Library accepted invalid color count");
        }
    });

    Check("CLI forwards custom colors and weights to both image pipelines and JSON", () =>
    {
        foreach (var method in new[] { "median-cut", "kmeans-lab", "kmeans-linear" })
        foreach (bool sprite in new[] { false, true })
        {
            string output = Path.Combine(directory, $"weighted-{method}-{sprite}.png");
            string[] arguments = [input, "-o", output, "--width", "12", "--height", "10",
                "--palette", "none", "--quantization", method, "--quantization-colors=8", "--color-weights", "--json",
                .. sprite ? new[] { "--sprite" } : Array.Empty<string>()];
            using var report = JsonDocument.Parse(ExpectSuccess(arguments));
            var options = report.RootElement.GetProperty("options");
            Require(options.GetProperty("quantizationColors").GetInt32() == 8 && options.GetProperty("useColorWeights").GetBoolean(), "Settings missing from JSON");
            CompareWithLibrary(input, output, CliArguments.Parse(arguments).Options);
            using var actual = new Bitmap(output);
            Require(DistinctColors(actual) <= 8, "Output exceeds requested palette budget");
        }
        Require(CliArguments.Parse([input, "--quantization-colors", "4096"]).Options.QuantizationColors == 4096, "CLI upper bound");
    });

    Check("Weighted sprite histogram includes partial alpha and handles empty images", () =>
    {
        using var source = new Bitmap(100, 1, PixelFormat.Format32bppArgb);
        for (int x = 0; x < source.Width; x++)
            source.SetPixel(x, 0, x == 0 ? Color.Red : Color.FromArgb(127, 0, 255, 0));
        foreach (var method in Enum.GetValues<QuantizationMethod>())
        {
            var options = new DownscaleOptions { SpriteMode = true, TargetWidth = 100, TargetHeight = 1,
                QuantizationColors = 1, UseColorWeights = true, Quantization = method, Palette = PaletteKind.None };
            var result = new PixelArtDownscaler().Process(source, options);
            using var output = result.Downscaled; using var crop = result.CroppedSource;
            Require((output.GetPixel(0, 0).ToArgb() & 0xFFFFFF) == (Color.Lime.ToArgb() & 0xFFFFFF), "Visible partial-alpha colors were excluded from the histogram");
            Require(output.GetPixel(0, 0).A == 255 && output.GetPixel(1, 0).A == 127, "Sprite quantization changed alpha");
            using var empty = new Bitmap(100, 1, PixelFormat.Format32bppArgb);
            var blank = new PixelArtDownscaler().Process(empty, options);
            using var blankOutput = blank.Downscaled; using var blankCrop = blank.CroppedSource;
            Require(blank.DominantColor.A == 0 && blankOutput.GetPixel(0, 0).A == 0, "Empty weighted sprite failed");
        }
    });

    Check("CLI resize, crop and preview PNG exports preserve every alpha value", () =>
    {
        string path = Path.Combine(directory, "alpha-ramp.png");
        using var source = new Bitmap(512, 4, PixelFormat.Format32bppArgb);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                source.SetPixel(x, y, Color.FromArgb(x / 2, 210, 70, 30));
        source.Save(path, ImageFormat.Png);
        for (int mode = 0; mode < 3; mode++)
        {
            string output = Path.Combine(directory, $"alpha-{mode}.png");
            string preview = Path.Combine(directory, $"alpha-preview-{mode}.png");
            string crop = Path.Combine(directory, $"alpha-crop-{mode}.png");
            var arguments = new List<string> { path, "-o", output, "--width", "256", "--height", "2" };
            if (mode == 0) arguments.Insert(0, "resize");
            else
            {
                arguments.AddRange(["--palette", "none", "--quantization-colors", "1",
                    "--preview-output", preview, "--preview-scale", "3", "--cropped-output", crop]);
                if (mode == 2) arguments.Add("--sprite");
            }
            ExpectSuccess(arguments.ToArray());
            using var actual = new Bitmap(output);
            for (int y = 0; y < actual.Height; y++)
                for (int x = 0; x < actual.Width; x++)
                {
                    var color = actual.GetPixel(x, y);
                    Require(color.A == x, $"Mode {mode}: alpha {x} became {color.A}");
                    if (x > 0) Require(color.R == 210 && color.G == 70 && color.B == 30, "PNG RGB was composited");
                }
            if (mode == 0) continue;
            using var enlarged = new Bitmap(preview);
            using var cropped = new Bitmap(crop);
            EqualPixels(source, cropped);
            for (int x = 0; x < enlarged.Width; x++)
                Require(enlarged.GetPixel(x, 0).ToArgb() == actual.GetPixel(x / 3, 0).ToArgb(), "Preview changed RGBA");
        }
    });

    Check("Contour cleanup requires opposing gradients and validates its settings", () =>
    {
        string path = Path.Combine(directory, "contour-source.png");
        string target = Path.Combine(directory, "contour-clean.png");
        string candidates = Path.Combine(directory, "contour-candidates.png");
        using (var fixture = new Bitmap(25, 21, PixelFormat.Format32bppArgb))
        {
            for (int y = 0; y < fixture.Height; y++)
                for (int x = 0; x < fixture.Width; x++)
                    fixture.SetPixel(x, y, x < 12 ? Color.Black : Color.White);
            fixture.SetPixel(12, 10, Color.Black);
            fixture.Save(path, ImageFormat.Png);
        }
        var run = Run(["clean-contours", path, "-o", target, "--window-size", "7",
            "--candidates-output", candidates, "--json"]);
        Require(run.Code == 0 && run.Error.Length == 0, run.Error);
        using var result = new Bitmap(target);
        using var map = new Bitmap(candidates);
        Require(result.GetPixel(12, 10).ToArgb() == Color.Black.ToArgb(),
            "A pixel without an opposing similar-color gradient was changed");
        Require(result.GetPixel(11, 10).ToArgb() == Color.Black.ToArgb(), "Correct side of contour changed");
        Require(map.GetPixel(12, 10).A == 0, "Pixel without opposing gradient became a candidate");
        using var report = JsonDocument.Parse(run.Output);
        Require(report.RootElement.GetProperty("changedPixels").GetInt32() == 0, "Unexpected replacement");
        Require(report.RootElement.GetProperty("windowSize").GetInt32() == 7, "Window size not reported");

        foreach (int size in new[] { 3, 5, 15 })
        {
            string endpoint = Path.Combine(directory, $"contour-clean-{size}.png");
            ExpectSuccess(["clean-contours", path, "-o", endpoint, "--window-size", size.ToString()]);
            using var endpointImage = new Bitmap(endpoint);
            Require(endpointImage.GetPixel(12, 10).ToArgb() == Color.Black.ToArgb(),
                $"Window {size} ignored the opposing-gradient rule");
        }

        Require(Run(["clean-contours", path, "-o", target]).Code == 1, "Existing output accepted");
        Require(Run(["clean-contours", path, "-o", target, "--window-size", "6", "--overwrite"]).Code == 2,
            "Even analysis window accepted");
        Require(Run(["clean-contours", path, "-o", target, "--window-size", "1", "--overwrite"]).Code == 2,
            "Analysis window below 3 × 3 accepted");
        Require(Run(["clean-contours", path, "-o", target, "--candidates-output", target, "--overwrite"]).Code == 2,
            "Output and candidate map may collide");
        Require(Run(["clean-contours", path, "-o", target, "--min-opposition", "101", "--overwrite"]).Code == 2,
            "Invalid opposition threshold accepted");
        Require(Run(["clean-contours", "--help"]).Code == 0, "Contour help missing");
    });

    Check("Contour cleanup preserves clean fine lines and every alpha value", () =>
    {
        string path = Path.Combine(directory, "contour-details.png");
        string target = Path.Combine(directory, "contour-details-clean.png");
        using (var fixture = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
        {
            for (int y = 0; y < fixture.Height; y++)
                for (int x = 0; x < fixture.Width; x++)
                    fixture.SetPixel(x, y, Color.FromArgb((x * 8 + y) % 256, x < 16 ? 25 : 220,
                        x < 16 ? 25 : 220, x < 16 ? 25 : 220));
            fixture.Save(path, ImageFormat.Png);
        }
        ExpectSuccess(["clean-contours", path, "-o", target, "--strictness", "strong"]);
        using var original = new Bitmap(path);
        using var result = new Bitmap(target);
        var alphas = new HashSet<int>();
        for (int y = 0; y < result.Height; y++)
            for (int x = 0; x < result.Width; x++)
            {
                Require(result.GetPixel(x, y).A == original.GetPixel(x, y).A,
                    $"Alpha changed at {x},{y}");
                alphas.Add(result.GetPixel(x, y).A);
            }
        Require(alphas.Count == 256, "Fixture did not cover every alpha value");

        string linePath = Path.Combine(directory, "contour-thin-line.png");
        string lineTarget = Path.Combine(directory, "contour-thin-line-clean.png");
        using (var line = new Bitmap(31, 31, PixelFormat.Format32bppArgb))
        {
            using var graphics = Graphics.FromImage(line);
            graphics.Clear(Color.White);
            for (int q = 3; q < 28; q++) line.SetPixel(q, q, Color.Black);
            line.Save(linePath, ImageFormat.Png);
        }
        ExpectSuccess(["clean-contours", linePath, "-o", lineTarget, "--strictness", "strong"]);
        using (var cleanLine = new Bitmap(linePath))
        using (var actualLine = new Bitmap(lineTarget))
            EqualPixels(cleanLine, actualLine);

        string stairPath = Path.Combine(directory, "contour-staircase.png");
        string stairTarget = Path.Combine(directory, "contour-staircase-clean.png");
        using (var stairs = new Bitmap(31, 31, PixelFormat.Format32bppArgb))
        {
            for (int y = 0; y < stairs.Height; y++)
                for (int x = 0; x < stairs.Width; x++)
                    stairs.SetPixel(x, y, x < y ? Color.Black : Color.White);
            stairs.Save(stairPath, ImageFormat.Png);
        }
        ExpectSuccess(["clean-contours", stairPath, "-o", stairTarget]);
        using (var cleanStairs = new Bitmap(stairPath))
        using (var actualStairs = new Bitmap(stairTarget))
            EqualPixels(cleanStairs, actualStairs);
        Require(Run(["clean-contours", path, "-o", target, "--strictness", "unknown", "--overwrite"]).Code == 2,
            "Unknown strictness accepted");
    });

    Check("Contour analysis shows candidates and gradients without changing the source", () =>
    {
        using var source = new Bitmap(9, 9, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.White);
        for (int y = 4; y <= 5; y++)
            for (int x = 4; x <= 5; x++) source.SetPixel(x, y, Color.Black);
        using var unchanged = (Bitmap)source.Clone();
        var analysis = ContourNoiseCleaner.Analyze(source, new ContourCleanOptions
        {
            WindowSize = 3, ProtectDetails = false, MinimumAgreement = 0, MinimumOpposition = 0
        });
        using var heatmap = analysis.Heatmap;
        Require(analysis.CandidateCount >= 1, "Contour candidate was not found");
        Require(heatmap.GetPixel(4, 4).ToArgb() == Color.Red.ToArgb(), "Candidate is not red");
        bool hasGradient = false;
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                Color pixel = heatmap.GetPixel(x, y);
                Require(pixel.A == source.GetPixel(x, y).A, $"Heatmap alpha changed at {x},{y}");
                if (pixel.A == 0) continue;
                Require(pixel.R == 255 && pixel.G == pixel.B, $"Heatmap left the white-red scale at {x},{y}");
                if (pixel.G is > 0 and < 255) hasGradient = true;
            }
        Require(hasGradient, "Contour gradients are absent from the heatmap");
        EqualPixels(unchanged, source);
    });

    Check("Contour candidate thresholds can be tuned independently", () =>
    {
        using var source = new Bitmap(9, 9, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.White);
        source.SetPixel(4, 4, Color.Black);
        source.SetPixel(5, 4, Color.FromArgb(12, 12, 12));
        source.SetPixel(4, 5, Color.FromArgb(18, 18, 18));
        source.SetPixel(5, 5, Color.FromArgb(24, 24, 24));
        ContourCleanOptions Options(double colorGap, int neighborCount,
            double pixelGradient = 120, double neighborGradient = 80,
            double agreement = 0, double opposition = 0) => new()
        {
            WindowSize = 3, ProtectDetails = false, SimilarColorDistance = colorGap,
            MinimumSimilarNeighbors = neighborCount, MinimumPixelGradient = pixelGradient,
            MinimumNeighborGradient = neighborGradient, MinimumAgreement = agreement,
            MinimumOpposition = opposition
        };
        using var found = ContourNoiseCleaner.Analyze(source, Options(24, 2)).Heatmap;
        using var noColorGap = ContourNoiseCleaner.Analyze(source, Options(0, 2)).Heatmap;
        using var tooManyNeighbors = ContourNoiseCleaner.Analyze(source, Options(24, 4)).Heatmap;
        using var highPixelGradient = ContourNoiseCleaner.Analyze(source, Options(24, 2, pixelGradient: 20000)).Heatmap;
        using var highNeighborGradient = ContourNoiseCleaner.Analyze(source, Options(24, 2, neighborGradient: 20000)).Heatmap;
        using var highAgreement = ContourNoiseCleaner.Analyze(source, Options(24, 2, agreement: 1)).Heatmap;
        using var highOpposition = ContourNoiseCleaner.Analyze(source, Options(24, 2, opposition: 1)).Heatmap;
        Require(found.GetPixel(4, 4).ToArgb() == Color.Red.ToArgb(), "Expected gradient candidate missing");
        Require(noColorGap.GetPixel(4, 4).ToArgb() != Color.Red.ToArgb(), "Color gap had no effect");
        Require(tooManyNeighbors.GetPixel(4, 4).ToArgb() != Color.Red.ToArgb(), "Neighbor count had no effect");
        Require(highPixelGradient.GetPixel(4, 4).ToArgb() != Color.Red.ToArgb(), "Pixel gradient threshold had no effect");
        Require(highNeighborGradient.GetPixel(4, 4).ToArgb() != Color.Red.ToArgb(), "Neighbor gradient threshold had no effect");
        Require(highAgreement.GetPixel(4, 4).ToArgb() != Color.Red.ToArgb(), "Agreement threshold had no effect");
        Require(highOpposition.GetPixel(4, 4).ToArgb() != Color.Red.ToArgb(), "Opposition threshold had no effect");
    });

    Check("Contour heatmap gives distant LAB colors stronger weight", () =>
    {
        int[] contrast = new int[2];
        int[] shades = [130, 210];
        for (int q = 0; q < shades.Length; q++)
        {
            using var source = new Bitmap(7, 7, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.FromArgb(90, 90, 90));
            source.SetPixel(4, 3, Color.FromArgb(shades[q], shades[q], shades[q]));
            var analysis = ContourNoiseCleaner.Analyze(source, new ContourCleanOptions { WindowSize = 3 });
            using var heatmap = analysis.Heatmap;
            contrast[q] = 255 - heatmap.GetPixel(3, 3).G;
        }
        Require(contrast[0] > 0 && contrast[1] >= contrast[0] * 4,
            $"Distant color should dominate the squared gradient: {contrast[0]} vs {contrast[1]}");
    });

    int variant = 0;
    foreach (var palette in Enum.GetValues<PaletteKind>())
    foreach (var method in Enum.GetValues<QuantizationMethod>())
    foreach (var mode in Enum.GetValues<BlockSelectionMode>())
    {
        int index = variant++;
        Check($"Pixel parity: {palette}, {method}, {mode}", () =>
        {
            string output = Path.Combine(directory, $"variant-{index}.png");
            string crop = Path.Combine(directory, $"crop-{index}.bmp");
            var horizontal = (CropHorizontalAlignment)(index % 3);
            var vertical = (CropVerticalAlignment)((index / 3) % 3);
            bool dither = palette is not PaletteKind.None and not PaletteKind.Step;
            int threads = index % 2 == 0 ? 1 : Environment.ProcessorCount;
            var arguments = new List<string>
            {
                "--input", input, "--output", output, "--width=12", "--height", "10",
                "--palette", palette.ToString(), "--quantization", method switch
                {
                    QuantizationMethod.MedianCut => "median-cut",
                    QuantizationMethod.KMeansLab => "kmeans-lab",
                    _ => "kmeans-linear"
                },
                "--block-mode", mode.ToString(), "--crop-horizontal", horizontal.ToString(),
                "--crop-vertical", vertical.ToString(), "--threads", threads.ToString(),
                "--cropped-output", crop, "--json"
            };
            if (palette == PaletteKind.Step)
                arguments.AddRange(["--palette-step", "51"]);
            if (dither)
                arguments.Add("--dithering");
            if (mode == BlockSelectionMode.Manual)
                arguments.AddRange(["--brightness", "20", "--contrast", "70", "--saturation", "90", "--edge", "30"]);

            string report = ExpectSuccess(arguments.ToArray());
            using var json = JsonDocument.Parse(report);
            Require(json.RootElement.GetProperty("outputSize").GetProperty("width").GetInt32() == 12, "Report width");
            Require(json.RootElement.GetProperty("stageTimingsSeconds").TryGetProperty("dither", out _) == dither, "Dithering stage");
            var expected = new DownscaleOptions
            {
                TargetWidth = 12, TargetHeight = 10, Palette = palette,
                PaletteStep = palette == PaletteKind.Step ? 51 : 32,
                Quantization = method, EnableDithering = dither, ThreadCount = threads,
                CropHorizontal = horizontal, CropVertical = vertical, BlockMode = mode,
                ManualCriteria = mode == BlockSelectionMode.Manual ? new ManualBlockCriteria
                {
                    TargetBrightness = 0.2, TargetContrast = 0.7, TargetSaturation = 0.9, TargetEdge = 0.3
                } : null
            };
            CompareWithLibrary(input, output, expected, crop);
        });
    }

    Check("Manual defaults and slider endpoints", () =>
    {
        var manual = CliArguments.Parse([input, "--block-mode", "manual"]).Options.ManualCriteria!;
        Require(manual.TargetBrightness == 0.5 && manual.TargetContrast == 0.5 &&
                manual.TargetSaturation == 0.5 && manual.TargetEdge == 0.5, "Manual defaults");
        manual = CliArguments.Parse([input, "--block-mode", "manual", "--brightness", "0",
            "--contrast", "100", "--saturation", "0", "--edge", "100"]).Options.ManualCriteria!;
        Require(manual.TargetBrightness == 0 && manual.TargetContrast == 1 &&
                manual.TargetSaturation == 0 && manual.TargetEdge == 1, "Slider endpoints");
    });

    Check("Nearest-neighbor preview and nested output directories", () =>
    {
        string output = Path.Combine(directory, "new folder", "pixels.png");
        string preview = Path.Combine(directory, "new folder", "preview.png");
        ExpectSuccess([input, "-o", output, "--width", "12", "--height", "10",
            "--preview-output", preview, "--preview-scale", "3"]);
        using var small = new Bitmap(output);
        using var large = new Bitmap(preview);
        Require(large.Width == 36 && large.Height == 30, "Preview size");
        for (int y = 0; y < large.Height; y++)
            for (int x = 0; x < large.Width; x++)
                Require(large.GetPixel(x, y).ToArgb() == small.GetPixel(x / 3, y / 3).ToArgb(), "Preview interpolation");
    });

    foreach (string extension in new[] { "png", "jpg", "jpeg", "bmp" })
    {
        Check($"Output encoding: {extension}", () =>
        {
            string path = Path.Combine(directory, $"format.{extension}");
            ExpectSuccess(["-i", input, "-o", path, "--width", "12", "--height", "10"]);
            using var bitmap = new Bitmap(path);
            Guid expected = extension switch
            {
                "png" => ImageFormat.Png.Guid,
                "bmp" => ImageFormat.Bmp.Guid,
                _ => ImageFormat.Jpeg.Guid
            };
            Require(bitmap.RawFormat.Guid == expected && bitmap.Width == 12 && bitmap.Height == 10, "Image encoding/size");
        });
    }

    Check("Reject invalid arguments without creating an output", () =>
    {
        string output = Path.Combine(directory, "must-not-exist.png");
        string[][] invalid =
        [
            ["--width", "0"], ["--height", "-1"],
            ["--threads", "0"], ["--threads", (Environment.ProcessorCount + 1).ToString()],
            ["--width", "no"], ["--width", "999999999999"], ["--width", "10.5"],
            ["--palette", "unknown"], ["--palette", "1"], ["--quantization", "unknown"],
            ["--quantization-colors", "0"], ["--quantization-colors", "4097"], ["--quantization-colors", "2.5"],
            ["--quantization-colors"], ["--color-weights=true"], ["--color-weights", "--color-weights"],
            ["--crop-horizontal", "top"], ["--crop-vertical", "left"], ["--block-mode", "auto"],
            ["--palette", "step", "--palette-step", "256"], ["--palette", "step", "--palette-step", "0"],
            ["--palette-step", "32"], ["--palette", "none", "--dithering"], ["--palette", "step", "--dithering"],
            ["--brightness", "50"], ["--block-mode", "manual", "--edge", "101"],
            ["--block-mode", "manual", "--saturation", "-1"], ["--preview-scale", "3"],
            ["--preview-output", Path.Combine(directory, "preview-invalid.png"), "--preview-scale", "0"],
            ["--unknown"], ["--width"], ["--width", "--height", "4"], ["--width="],
            ["--width", "12", "--width", "12"], ["--json=true"], ["--json", "--json"], ["extra.png"]
        ];
        foreach (var extra in invalid)
        {
            var run = Run([input, "-o", output, .. extra]);
            Require(run.Code == 2 && run.Output.Length == 0 && run.Error.Length > 0,
                $"Expected usage error for {string.Join(' ', extra)}; got {run.Code}: {run.Error}");
            Require(!File.Exists(output), "Invalid arguments created output");
        }
        Require(Run(["--width", "12"]).Code == 2, "Missing input");
        Require(Run([input, "-o", Path.Combine(directory, "bad.gif")]).Code == 2, "Unsupported output extension");
        Require(Run([input, "-o", output, "--width", "68"]).Code == 2, "Upscaling accepted");
    });

    Check("No overwrite by default; explicit replacement; source protection", () =>
    {
        string output = Path.Combine(directory, "existing.png");
        File.WriteAllText(output, "keep me");
        Require(Run([input, "-o", output]).Code == 1, "Existing output accepted");
        Require(File.ReadAllText(output) == "keep me", "Existing output modified");
        ExpectSuccess([input, "-o", output, "--overwrite"]);
        using (var bitmap = new Bitmap(output))
            Require(bitmap.Width == 48, "Replacement not written");
        byte[] before = File.ReadAllBytes(input);
        Require(Run([input, "-o", input, "--overwrite"]).Code == 2, "Source overwrite accepted");
        Require(before.SequenceEqual(File.ReadAllBytes(input)), "Source modified");
        Require(Run([input, "-o", output, "--preview-output", output.ToUpperInvariant(), "--overwrite"]).Code == 2,
            "Duplicate output paths accepted");
        string untouched = Path.Combine(directory, "untouched.png");
        Require(Run([input, "-o", untouched, "--cropped-output", output]).Code == 1, "Existing extra export accepted");
        Require(!File.Exists(untouched), "Main output created before extra output validation");
    });

    Check("Missing, corrupt input and unwritable output are file errors", () =>
    {
        string output = Path.Combine(directory, "errors.png");
        var missing = Run([Path.Combine(directory, "missing.png"), "-o", output]);
        Require(missing.Code == 1 && missing.Output.Length == 0 && missing.Error.Length > 0, "Missing input");
        string corrupt = Path.Combine(directory, "corrupt.png");
        File.WriteAllText(corrupt, "not an image");
        Require(Run([corrupt, "-o", output]).Code == 1, "Corrupt image");
        string obstruction = Path.Combine(directory, "file-as-directory");
        File.WriteAllText(obstruction, "keep me");
        Require(Run([input, "-o", Path.Combine(obstruction, "output.png")]).Code == 1, "Unwritable output");
        Require(!File.Exists(output), "Failure created an output");
    });

    Check("End-of-options marker supports a leading-dash input", () =>
    {
        var parsed = CliArguments.Parse(["--width=12", "--", "--source.png"]);
        Require(Path.GetFileName(parsed.Input) == "--source.png" && parsed.Options.TargetWidth == 12, "End-of-options parsing");
    });
}
finally
{
    Directory.Delete(directory, recursive: true);
}

Console.WriteLine($"{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;

void Check(string name, Action test)
{
    try
    {
        test();
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void RequireThrows<TException>(Action action, string message) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException(message);
}

static void RequireWorkflowStep(int index, string operationId, Action action)
{
    try { action(); }
    catch (WorkflowStepException ex)
    {
        Require(ex.StepIndex == index && ex.OperationId == operationId, "Workflow validation lost step metadata");
        return;
    }
    throw new InvalidOperationException("Invalid workflow step was accepted");
}

static (int Code, string Output, string Error) Run(string[] arguments)
{
    using var output = new StringWriter();
    using var error = new StringWriter();
    int code = CliApplication.Run(arguments, output, error);
    return (code, output.ToString(), error.ToString());
}

static string ExpectSuccess(string[] arguments)
{
    var run = Run(arguments);
    Require(run.Code == 0 && run.Error.Length == 0, $"Exit {run.Code}: {run.Error}");
    return run.Output;
}

static void CompareWithLibrary(string input, string output, DownscaleOptions options, string? cropPath = null)
{
    using var source = new Bitmap(input);
    var result = new PixelArtDownscaler().Process(source, options);
    using var expected = result.Downscaled;
    using var expectedCrop = result.CroppedSource;
    using var actual = new Bitmap(output);
    EqualPixels(expected, actual);
    if (cropPath is not null)
    {
        using var actualCrop = new Bitmap(cropPath);
        EqualPixels(expectedCrop, actualCrop);
    }
}

static void EqualPixels(Bitmap expected, Bitmap actual)
{
    Require(expected.Size == actual.Size, "Bitmap dimensions differ");
    for (int y = 0; y < expected.Height; y++)
        for (int x = 0; x < expected.Width; x++)
            Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), $"Pixel differs at {x},{y}");
}

static int[] ReadIcoSizes(string path)
{
    using var stream = File.OpenRead(path);
    using var reader = new BinaryReader(stream);
    Require(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1, "Invalid ICO header");
    int count = reader.ReadUInt16();
    var sizes = new int[count];
    for (int q = 0; q < count; q++)
    {
        byte width = reader.ReadByte();
        sizes[q] = width == 0 ? 256 : width;
        Require(reader.ReadBytes(15).Length == 15, "Truncated ICO directory");
    }
    return sizes;
}
static byte[] MinimalAseprite()
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write(new byte[128]);
    long frameStart = stream.Position;
    writer.Write(0); writer.Write((ushort)0xF1FA); writer.Write((ushort)2);
    writer.Write((ushort)100); writer.Write((ushort)0); writer.Write((uint)0);
    byte[] name = System.Text.Encoding.UTF8.GetBytes("Layer");
    writer.Write(6 + 18 + name.Length);
    writer.Write((ushort)0x2004);
    writer.Write((ushort)3); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(0);
    writer.Write((ushort)0); writer.Write((byte)255); writer.Write(new byte[3]);
    writer.Write((ushort)name.Length); writer.Write(name);
    writer.Write(6 + 16 + 4 + 16);
    writer.Write((ushort)0x2005);
    writer.Write((ushort)0); writer.Write((short)0); writer.Write((short)0);
    writer.Write((byte)255); writer.Write((ushort)0); writer.Write((short)0); writer.Write(new byte[5]);
    writer.Write((ushort)2); writer.Write((ushort)2);
    writer.Write(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 0, 0, 0, 0 });
    long size = stream.Length;
    stream.Position = frameStart;
    writer.Write((int)(size - frameStart));
    stream.Position = 0;
    writer.Write((uint)size); writer.Write((ushort)0xA5E0); writer.Write((ushort)1);
    writer.Write((ushort)2); writer.Write((ushort)2); writer.Write((ushort)32);
    writer.Write((uint)1); writer.Write((ushort)100);
    stream.Position = 34; writer.Write((byte)1); writer.Write((byte)1);
    return stream.ToArray();
}
static int DistinctColors(Bitmap bitmap)
{
    var colors = new HashSet<int>();
    for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
            colors.Add(bitmap.GetPixel(x, y).ToArgb());
    return colors.Count;
}

sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
