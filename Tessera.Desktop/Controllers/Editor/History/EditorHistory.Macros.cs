using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

internal sealed partial class EditorHistory
{
    private readonly GroupBox _macrosGroup = new();

    internal GroupBox MacrosGroup => _macrosGroup;

    private readonly ListBox _macroList = new() { DisplayMemberPath = "Name", MinHeight = 84, MaxHeight = 220 };

    private readonly ListBox _macroSteps = new() { MinHeight = 80, MaxHeight = 260 };

    private readonly ObservableCollection<MacroDefinition> _macros = [];

    private readonly Button _macroApply = new(), _macroApplyVideo = new(), _macroCancel = new();

    private MacroStore _macroStore = null!;

    private CancellationTokenSource? _macroCancellation;

    internal bool IsMacroRunning => _macroCancellation is not null;

    internal void InitializeMacros()
    {
        _macroStore = new MacroStore(Host._library.DirectoryPath);
        _macrosGroup.Header = Localization.Text("Macros");
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = Localization.Text("SharedByAllProjectsInThis"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        _macroList.ItemsSource = _macros;
        AutomationProperties.SetName(_macroList, Localization.Text("LibraryMacros"));
        AutomationProperties.SetName(_macroSteps, Localization.Text("MacroSteps"));
        content.Children.Add(_macroList);
        content.Children.Add(new TextBlock { Text = Localization.Text("StepSequence"), Margin = new Thickness(0, 14, 0, 6) });
        content.Children.Add(_macroSteps);
        _macroApply.Content = Localization.Text("Apply"); _macroApply.Style = (Style)_editor.FindResource("PrimaryButton"); _macroApply.Margin = new Thickness(0, 10, 0, 0);
        _macroApplyVideo.Content = Localization.Text("ApplyMacroToVideo"); _macroApplyVideo.Margin = new Thickness(0, 6, 0, 0);
        _macroCancel.Content = Localization.Text("CancelLabel"); _macroCancel.Margin = new Thickness(0, 6, 0, 0); _macroCancel.Visibility = Visibility.Collapsed;
        _macroApply.Click += async (_, _) => await ApplySelectedMacroAsync();
        _macroApplyVideo.Click += async (_, _) => await ApplySelectedMacroToVideoAsync();
        _macroCancel.Click += (_, _) => _macroCancellation?.Cancel();
        content.Children.Add(_macroApply); content.Children.Add(_macroApplyVideo); content.Children.Add(_macroCancel);
        _macrosGroup.Content = content; _editor.settingsPanel.Children.Add(_macrosGroup);
        _macroList.SelectionChanged += (_, _) =>
        {
            _macroSteps.ItemsSource = (_macroList.SelectedItem as MacroDefinition)?.Steps.Select((s, q) => $"{q + 1}. {StepName(s.OperationId)}").ToArray();
            _macroApply.IsEnabled = _macroList.SelectedItem is MacroDefinition && !_editor._processing;
            _macroApplyVideo.IsEnabled = _macroList.SelectedItem is MacroDefinition && !_editor._processing;
        };
        _macroList.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (_editor._processing || ItemsControl.ContainerFromElement(_macroList, e.OriginalSource as DependencyObject) is not ListBoxItem { Content: MacroDefinition macro } item) return;
            item.IsSelected = true;
            var menu = new ContextMenu();
            menu.Resources.MergedDictionaries.Add(_editor.Resources);
            var rename = new MenuItem { Header = Localization.Text("RenameAction") };
            rename.Click += (_, _) => RenameMacro(macro);
            var delete = new MenuItem { Header = Localization.Text("Delete") };
            delete.Click += (_, _) =>
            {
                try { _macroStore.Delete(macro.Id); RefreshMacros(); }
                catch (Exception ex) { ShowMacroError(ex.Message); }
            };
            menu.Items.Add(rename); menu.Items.Add(delete);
            menu.PlacementTarget = item; menu.IsOpen = true; e.Handled = true;
        };
        _editor.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && _macroCancellation is not null) { _macroCancellation.Cancel(); e.Handled = true; } };
        RefreshMacros();
    }

    internal static string StepName(string operation) => operation switch
    {
        ProcessingOperations.Profile => Localization.Text("ProfileProcessing"),
        ProcessingOperations.Resize => Localization.Text("SizeAndFrame"),
        ProcessingOperations.Trim => Localization.Text("TrimTransparentEdgesLabel"),
        ProcessingOperations.Colors => Localization.Text("Palette"),
        ProcessingOperations.Cleanup => Localization.Text("Smoothing"),
        ProcessingOperations.Align => Localization.Text("GridAlignment"),
        ProcessingOperations.AlignLocal => Localization.Text("LocalGridAlignment"),
        ProcessingOperations.AlignLocalSnapped => Localization.Text("LocalGridWithSnapping"),
        ProcessingOperations.Reduce => Localization.Text("GridReduction"),
        ProcessingOperations.Monochrome => Localization.Text("Monochrome"),
        ProcessingOperations.Sharpen => Localization.Text("Sharpen"),
        ProcessingOperations.Ascii => Localization.Text("AsciiImage"),
        ProcessingOperations.ReplaceColor => Localization.Text("ColorReplacement"),
        ProcessingOperations.AdjustChannel => Localization.Text("RGBAChannels"),
        ProcessingOperations.RemoveBackground => Localization.Text("BackgroundRemoval"),
        ProcessingOperations.FinalistReduce => Localization.Text("TCRReduceImage"),
        _ => operation
    };

    private void RefreshMacros(Guid? selected = null)
    {
        selected ??= (_macroList.SelectedItem as MacroDefinition)?.Id;
        _macros.Clear(); foreach (var macro in _macroStore.List()) _macros.Add(macro);
        _macroList.SelectedItem = _macros.FirstOrDefault(m => m.Id == selected) ?? _macros.FirstOrDefault();
        _macroApply.IsEnabled = _macroList.SelectedItem is MacroDefinition && !_editor._processing;
        _macroApplyVideo.IsEnabled = _macroList.SelectedItem is MacroDefinition && !_editor._processing;
        if (_macroStore.LoadWarnings.Count > 0)
        { Host.statusLabel.Text = Localization.Text("CouldNotReadMacrosPrefix") + string.Join("; ", _macroStore.LoadWarnings); }
    }

    private void CreateMacro(GenerationEntry generation)
    {
        if (_editor._processing || _editor._activeSource is null) return;
        if (!HistoryRecipes.TryBuild(_editor._activeSource, generation, out var recipe, out string reason))
        { ShowMacroError(reason); return; }
        string? name = PromptMacroName(Localization.Text("CreateMacro"));
        if (name is null) return;
        try
        {
            var macro = _macroStore.Save(new MacroDefinition { Name = name, Steps = recipe!.Steps });
            RefreshMacros(macro.Id);
            _editor.Dock.OpenEditorTool("macros", Localization.Text("Macros"), 460, _macrosGroup);
            Host.statusLabel.Text = Localization.Text("MacroSavedPrefix") + macro.Name;
        }
        catch (Exception ex) { ShowMacroError(ex.Message); }
    }

    private string? PromptMacroName(string title, string initial = "", Guid? except = null)
    {
        var dialog = new MacroNameDialog(title, initial, name => !_macroStore.List().Any(m => m.Id != except && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))) { Owner = Host };
        return dialog.ShowDialog() == true ? dialog.MacroName : null;
    }

    private void RenameMacro(MacroDefinition macro)
    {
        string? name = PromptMacroName(Localization.Text("RenameMacro"), macro.Name, macro.Id);
        if (name is null) return;
        try { _macroStore.Save(new MacroDefinition { Id = macro.Id, Name = name, Steps = macro.Steps }); RefreshMacros(macro.Id); }
        catch (Exception ex) { ShowMacroError(ex.Message); }
    }

    private void ShowMacroError(string reason)
    {
        Host.statusLabel.Text = reason;
        TesseraDialog.Show(Host, reason, Localization.Text("Macros"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async Task ApplySelectedMacroAsync()
    {
        if (_editor._processing || _editor._activeSource is null || _macroList.SelectedItem is not MacroDefinition selected) return;
        var document = _editor._activeSource;
        Guid? parentId = _editor.ProcessingBasis == 1 ? document.SelectedGeneration?.Id : null;
        var basis = _editor.ProcessingBasis == 1 ? _editor._downscaledResult : _editor._sourceImage;
        if (basis is null) return;
        var macro = WorkflowJson.Snapshot(selected);
        var applied = new ProcessingRecipe { Steps = macro.Steps };
        var settings = _editor.Tools.CaptureSettings();
        using var input = (Bitmap)basis.Clone();
        using var cancellation = new CancellationTokenSource(); _macroCancellation = cancellation;
        _editor.SetProcessingState(true);
        _macroList.IsEnabled = _macroApply.IsEnabled = _macroApplyVideo.IsEnabled = false; _macroCancel.Visibility = Visibility.Visible;
        var activeOperation = Host._mainProgress;
        var progress = new Progress<WorkflowProgress>(p =>
        {
            if (_editor._processing && ReferenceEquals(Host._mainProgress, activeOperation))
                Host.statusLabel.Text = Localization.Format("MacroStepProgress", p.Index, p.Count, StepName(p.OperationId));
        });
        try
        {
            using var output = await Task.Run(() => new WorkflowExecutor().Execute(input, applied, cancellation.Token, progress, Host.CurrentProcessingProgress));
            if (cancellation.IsCancellationRequested)
                throw new WorkflowCanceledException(applied.Steps.Count, applied.Steps[^1].OperationId, new OperationCanceledException(cancellation.Token));
            // Publish only after every step has completed. No partial history exists on cancellation.
            var generation = Host._library.SaveGeneration(document, output, null, new GenerationEntry
            {
                Operation = macro.Name,
                OperationId = ProcessingOperations.Macro,
                AppliedRecipe = applied,
                ParentGenerationId = parentId,
                Settings = settings,
                PreservesTransparency = true,
                Caption = macro.Name,
                Log = string.Join(Environment.NewLine, macro.Steps.Select((step, q) => $"{q + 1}. {StepName(step.OperationId)}"))
            });
            if (!CommitGeneration(document, generation)) return;
            Host.statusLabel.Text = Localization.Text("MacroAppliedPrefix") + macro.Name;
        }
        catch (WorkflowCanceledException ex) { Host.statusLabel.Text = Localization.Format("MacroCancelledAtStepFormat", ex.StepIndex, StepName(ex.OperationId)); }
        catch (OperationCanceledException) { Host.statusLabel.Text = Localization.Text("MacroCancelled"); }
        catch (WorkflowStepException ex) { ShowMacroError(Localization.Format("MacroStepFailed", ex.StepIndex, StepName(ex.OperationId), ex.InnerException?.Message ?? ex.Message)); }
        catch (Exception ex) { ShowMacroError(ex.Message); }
        finally
        {
            _macroCancellation = null; _editor.SetProcessingState(false);
            _macroList.IsEnabled = true; _macroApply.IsEnabled = _macroApplyVideo.IsEnabled = _macroList.SelectedItem is MacroDefinition;
            _macroCancel.Visibility = Visibility.Collapsed;
        }
    }

    private async Task ApplySelectedMacroToVideoAsync()
    {
        if (_editor._processing || _macroList.SelectedItem is not MacroDefinition selected) return;
        var inputDialog = new OpenFileDialog
        {
            Title = Localization.Text("SelectVideo"),
            Filter = Localization.Text("VideoInputFileFilter")
        };
        if (inputDialog.ShowDialog(Host) != true) return;
        var outputDialog = new SaveFileDialog
        {
            Title = Localization.Text("SaveProcessedVideo"),
            Filter = Localization.Text("VideoOutputFileFilter"),
            DefaultExt = ".mp4", AddExtension = true, OverwritePrompt = true,
            FileName = Path.GetFileNameWithoutExtension(inputDialog.FileName) + "_tessera.mp4"
        };
        if (outputDialog.ShowDialog(Host) != true) return;
        MacroDefinition macro = WorkflowJson.Snapshot(selected);
        var recipe = new ProcessingRecipe { Version = macro.Version, Steps = macro.Steps };
        using var cancellation = new CancellationTokenSource(); _macroCancellation = cancellation;
        _editor.SetProcessingState(true);
        _macroList.IsEnabled = _macroApply.IsEnabled = _macroApplyVideo.IsEnabled = false;
        _macroCancel.Visibility = Visibility.Visible;
        var progress = new Progress<VideoMacroProgress>(p => Host.statusLabel.Text = Localization.Format("VideoFramesProcessed", p.Frame));
        try
        {
            VideoMacroResult result = await Task.Run(() => new VideoMacroProcessor().Apply(inputDialog.FileName,
                outputDialog.FileName, recipe, overwrite: true, cancellationToken: cancellation.Token, progress: progress));
            Host.statusLabel.Text = Localization.Format("VideoSaved", result.Frames, outputDialog.FileName);
        }
        catch (OperationCanceledException) { Host.statusLabel.Text = Localization.Text("VideoProcessingCanceled"); }
        catch (Exception ex) { ShowMacroError(ex.Message); }
        finally
        {
            _macroCancellation = null; _editor.SetProcessingState(false);
            _macroList.IsEnabled = true;
            _macroApply.IsEnabled = _macroApplyVideo.IsEnabled = _macroList.SelectedItem is MacroDefinition;
            _macroCancel.Visibility = Visibility.Collapsed;
        }
    }
}
