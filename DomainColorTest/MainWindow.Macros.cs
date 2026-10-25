using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class MainWindow
{
    private readonly GroupBox _macrosGroup = new();
    private readonly ListBox _macroList = new() { DisplayMemberPath = "Name", MinHeight = 84, MaxHeight = 220 };
    private readonly ListBox _macroSteps = new() { MinHeight = 80, MaxHeight = 260 };
    private readonly ObservableCollection<MacroDefinition> _macros = [];
    private readonly Button _macroApply = new(), _macroCancel = new();
    private MacroStore _macroStore = null!;
    private CancellationTokenSource? _macroCancellation;

    private void InitializeMacros()
    {
        _macroStore = new MacroStore(_library.DirectoryPath);
        _macrosGroup.Header = Localization.Text("Макросы");
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = Localization.Text("Общие для всех проектов этой библиотеки"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        _macroList.ItemsSource = _macros;
        AutomationProperties.SetName(_macroList, Localization.Text("Макросы библиотеки"));
        AutomationProperties.SetName(_macroSteps, Localization.Text("Шаги макроса"));
        content.Children.Add(_macroList);
        content.Children.Add(new TextBlock { Text = Localization.Text("Последовательность шагов"), Margin = new Thickness(0, 14, 0, 6) });
        content.Children.Add(_macroSteps);
        _macroApply.Content = Localization.Text("Применить"); _macroApply.Style = (Style)FindResource("PrimaryButton"); _macroApply.Margin = new Thickness(0, 10, 0, 0);
        _macroCancel.Content = Localization.Text("Отменить"); _macroCancel.Margin = new Thickness(0, 6, 0, 0); _macroCancel.Visibility = Visibility.Collapsed;
        _macroApply.Click += async (_, _) => await ApplySelectedMacroAsync();
        _macroCancel.Click += (_, _) => _macroCancellation?.Cancel();
        content.Children.Add(_macroApply); content.Children.Add(_macroCancel);
        _macrosGroup.Content = content; settingsPanel.Children.Add(_macrosGroup);
        _macroList.SelectionChanged += (_, _) =>
        {
            _macroSteps.ItemsSource = (_macroList.SelectedItem as MacroDefinition)?.Steps.Select((s, q) => $"{q + 1}. {StepName(s.OperationId)}").ToArray();
            _macroApply.IsEnabled = _macroList.SelectedItem is MacroDefinition && !_processing;
        };
        _macroList.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (_processing || ItemsControl.ContainerFromElement(_macroList, e.OriginalSource as DependencyObject) is not ListBoxItem { Content: MacroDefinition macro } item) return;
            item.IsSelected = true;
            var menu = new ContextMenu();
            menu.Resources.MergedDictionaries.Add(Resources);
            var rename = new MenuItem { Header = Localization.Text("Переименовать…") };
            rename.Click += (_, _) => RenameMacro(macro);
            var delete = new MenuItem { Header = Localization.Text("Удалить") };
            delete.Click += (_, _) =>
            {
                try { _macroStore.Delete(macro.Id); RefreshMacros(); }
                catch (Exception ex) { ShowMacroError(ex.Message); }
            };
            menu.Items.Add(rename); menu.Items.Add(delete);
            menu.PlacementTarget = item; menu.IsOpen = true; e.Handled = true;
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && _macroCancellation is not null) { _macroCancellation.Cancel(); e.Handled = true; } };
        RefreshMacros();
    }

    internal static string StepName(string operation) => Localization.Text(operation switch
    {
        ProcessingOperations.Profile => "Профильная обработка",
        ProcessingOperations.Resize => "Размер и кадр",
        ProcessingOperations.Trim => "Обрезка прозрачных краёв",
        ProcessingOperations.Colors => "Число цветов",
        ProcessingOperations.Cleanup => "Очистка цветов",
        ProcessingOperations.Align => "Выравнивание сетки",
        ProcessingOperations.AlignLocal => "Локальное выравнивание сетки",
        ProcessingOperations.AlignLocalSnapped => "Локальная сетка с привязкой",
        ProcessingOperations.Reduce => "Сжатие сетки",
        ProcessingOperations.Monochrome => "Монохром",
        ProcessingOperations.Ascii => "ASCII-изображение",
        ProcessingOperations.ReplaceColor => "Замена цвета",
        ProcessingOperations.RemoveBackground => "Удаление фона",
        _ => operation
    });

    private void RefreshMacros(Guid? selected = null)
    {
        selected ??= (_macroList.SelectedItem as MacroDefinition)?.Id;
        _macros.Clear(); foreach (var macro in _macroStore.List()) _macros.Add(macro);
        _macroList.SelectedItem = _macros.FirstOrDefault(m => m.Id == selected) ?? _macros.FirstOrDefault();
        _macroApply.IsEnabled = _macroList.SelectedItem is MacroDefinition && !_processing;
        if (_macroStore.LoadWarnings.Count > 0)
        { statusLabel.Text = Localization.Text("Не удалось прочитать макросы: ") + string.Join("; ", _macroStore.LoadWarnings); }
    }

    private void CreateMacro(GenerationEntry generation)
    {
        if (_processing || _activeSource is null) return;
        if (!HistoryRecipes.TryBuild(_activeSource, generation, out var recipe, out string reason))
        { ShowMacroError(reason); return; }
        string? name = PromptMacroName(Localization.Text("Создать макрос"));
        if (name is null) return;
        try
        {
            var macro = _macroStore.Save(new MacroDefinition { Name = name, Steps = recipe!.Steps });
            RefreshMacros(macro.Id);
            OpenEditorTool("macros", Localization.Text("Макросы"), 460, _macrosGroup);
            statusLabel.Text = Localization.Text("Макрос сохранён: ") + macro.Name;
        }
        catch (Exception ex) { ShowMacroError(ex.Message); }
    }

    private string? PromptMacroName(string title, string initial = "", Guid? except = null)
    {
        var dialog = new MacroNameDialog(title, initial, name => !_macroStore.List().Any(m => m.Id != except && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.MacroName : null;
    }

    private void RenameMacro(MacroDefinition macro)
    {
        string? name = PromptMacroName(Localization.Text("Переименовать макрос"), macro.Name, macro.Id);
        if (name is null) return;
        try { _macroStore.Save(new MacroDefinition { Id = macro.Id, Name = name, Steps = macro.Steps }); RefreshMacros(macro.Id); }
        catch (Exception ex) { ShowMacroError(ex.Message); }
    }

    private void ShowMacroError(string reason)
    {
        statusLabel.Text = reason;
        TesseraDialog.Show(this, reason, Localization.Text("Макросы"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async Task ApplySelectedMacroAsync()
    {
        if (_processing || _activeSource is null || _macroList.SelectedItem is not MacroDefinition selected) return;
        var document = _activeSource;
        Guid? parentId = ProcessingBasis == 1 ? document.SelectedGeneration?.Id : null;
        var basis = ProcessingBasis == 1 ? _downscaledResult : _sourceImage;
        if (basis is null) return;
        var macro = WorkflowJson.Snapshot(selected);
        var applied = new ProcessingRecipe { Steps = macro.Steps };
        var settings = CaptureSettings();
        using var input = (Bitmap)basis.Clone();
        using var cancellation = new CancellationTokenSource(); _macroCancellation = cancellation;
        SetProcessingState(true);
        _macroList.IsEnabled = _macroApply.IsEnabled = false; _macroCancel.Visibility = Visibility.Visible;
        var progress = new Progress<WorkflowProgress>(p => statusLabel.Text = Localization.Format("Шаг {0}/{1}: {2}", p.Index, p.Count, StepName(p.OperationId)));
        try
        {
            using var output = await Task.Run(() => new WorkflowExecutor().Execute(input, applied, cancellation.Token, progress));
            if (cancellation.IsCancellationRequested)
                throw new WorkflowCanceledException(applied.Steps.Count, applied.Steps[^1].OperationId, new OperationCanceledException(cancellation.Token));
            // Publish only after every step has completed. No partial history exists on cancellation.
            var generation = _library.SaveGeneration(document, output, null, new GenerationEntry
            {
                Operation = macro.Name, OperationId = ProcessingOperations.Macro, AppliedRecipe = applied,
                ParentGenerationId = parentId, Settings = settings, PreservesTransparency = true,
                Caption = macro.Name, Log = string.Join(Environment.NewLine, macro.Steps.Select((step, q) => $"{q + 1}. {StepName(step.OperationId)}"))
            });
            document.Generations.Add(generation); SelectGeneration(generation);
            statusLabel.Text = Localization.Text("Макрос применён: ") + macro.Name;
        }
        catch (WorkflowCanceledException ex) { statusLabel.Text = Localization.Format("Макрос отменён на шаге {0}: {1}.", ex.StepIndex, StepName(ex.OperationId)); }
        catch (OperationCanceledException) { statusLabel.Text = Localization.Text("Макрос отменён. Итоговый результат не сохранён."); }
        catch (WorkflowStepException ex) { ShowMacroError(Localization.Format("Шаг {0}: {1}. {2}", ex.StepIndex, StepName(ex.OperationId), ex.InnerException?.Message ?? ex.Message)); }
        catch (Exception ex) { ShowMacroError(ex.Message); }
        finally
        {
            _macroCancellation = null; SetProcessingState(false);
            _macroList.IsEnabled = true; _macroApply.IsEnabled = _macroList.SelectedItem is MacroDefinition;
            _macroCancel.Visibility = Visibility.Collapsed;
        }
    }
}
