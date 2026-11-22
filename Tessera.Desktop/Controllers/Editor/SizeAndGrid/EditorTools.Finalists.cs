using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Tessera.Core.Imaging;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

internal sealed partial class EditorTools
{
    private readonly GroupBox _finalistGroup = new();

    internal GroupBox FinalistGroup => _finalistGroup;

    private readonly RadioButton _t3Mode = new(), _p5Mode = new(), _mixedMode = new();

    private readonly ComboBox _finalistFactor = new();

    private readonly Button _finalistApply = new();

    internal void SetFinalistEnabled(bool enabled) => _finalistApply.IsEnabled = enabled;

    internal void InitializeFinalistTool()
    {
        _finalistGroup.Header = Localization.Text("TCRReduceImage");
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = Localization.Text("OneOutputPixelPerSelectedBlock"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        AddMode(_t3Mode, "T3 / p44", "CellBoundariesShiftByHalfReduction");
        AddMode(_p5Mode, "P5 / OFF", "RegularCellsSelectedSizeR0C0");
        AddMode(_mixedMode, "50 / 50", "EqualSrgbBlendT3P44P5");
        _t3Mode.IsChecked = true;
        var factorRow = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        factorRow.Children.Add(new TextBlock
        {
            Text = Localization.Text("ReductionFactor264"),
            Width = 132,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });
        _finalistFactor.MaxDropDownHeight = 240;
        _finalistFactor.ItemsSource = Enumerable.Range(FinalistReducer.MinFactor,
            FinalistReducer.MaxFactor - FinalistReducer.MinFactor + 1);
        _finalistFactor.SelectedItem = FinalistReducer.DefaultFactor;
        AutomationProperties.SetName(_finalistFactor, Localization.Text("ReductionFactor264"));
        factorRow.Children.Add(_finalistFactor);
        content.Children.Add(factorRow);
        _finalistApply.Content = Localization.Text("ReduceImage");
        _finalistApply.Style = (Style)_editor.FindResource("PrimaryButton");
        _finalistApply.Margin = new Thickness(0, 14, 0, 0);
        _finalistApply.IsEnabled = false;
        AutomationProperties.SetName(_finalistApply, Localization.Text("ApplyTCRReduction"));
        _finalistApply.Click += (_, _) => ApplyFinalistReduction();
        content.Children.Add(_finalistApply);
        _finalistGroup.Content = content;
        _editor.settingsPanel.Children.Add(_finalistGroup);

        void AddMode(RadioButton button, string title, string explanation)
        {
            button.GroupName = "TcrFinalistMode";
            button.Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = Localization.Text(explanation), TextWrapping = TextWrapping.Wrap, MaxWidth = 210,
                        Foreground = (System.Windows.Media.Brush)_editor.FindResource("Ts.TextSecondary") }
                }
            };
            button.Margin = new Thickness(0, 0, 0, 10);
            AutomationProperties.SetName(button, title);
            content.Children.Add(button);
        }
    }

    private async void ApplyFinalistReduction()
    {
        Bitmap? basis = GetFilterBasis();
        if (_editor._processing || basis is null || _editor._activeSource is null) return;
        if (_finalistFactor.SelectedItem is not int factor || factor is < FinalistReducer.MinFactor or > FinalistReducer.MaxFactor)
        {
            Host.Notify(Localization.Text("EnterIntegerReductionFactor264"), NotificationKind.Warning);
            return;
        }
        var document = _editor._activeSource;
        Guid? parentId = ReferenceEquals(basis, _editor._downscaledResult) ? document.SelectedGeneration?.Id : null;
        FinalistMode mode = _p5Mode.IsChecked == true ? FinalistMode.P5Off :
            _mixedMode.IsChecked == true ? FinalistMode.HalfAndHalf : FinalistMode.T3P44;
        string modeName = mode switch
        {
            FinalistMode.T3P44 => "T3/p44",
            FinalistMode.P5Off => "P5/OFF",
            _ => "50/50"
        };
        var settings = CaptureSettings();
        using var input = (Bitmap)basis.Clone();
        _editor.SetProcessingState(true);
        Host.statusLabel.Text = Localization.Format("ReducingWithTCRAction", factor);
        try
        {
            var generation = await Task.Run(() =>
            {
                using var output = FinalistReducer.Reduce(input, mode, factor, Host.CurrentProcessingProgress);
                return Host._library.SaveGeneration(document, output, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = $"TCR {modeName} {factor}×",
                    OperationId = ProcessingOperations.FinalistReduce,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep
                    {
                        OperationId = ProcessingOperations.FinalistReduce,
                        FinalistMode = mode,
                        FinalistFactor = factor
                    }),
                    ParentGenerationId = parentId,
                    PreservesTransparency = true,
                    Caption = $"TCR {modeName} {factor}× · {output.Width} × {output.Height}",
                    Log = Localization.Format("TCRCellsAlpha0255Format",
                        modeName, input.Width, input.Height, output.Width, output.Height, factor)
                });
            });
            if (!_editor.History.CommitGeneration(document, generation)) return;
            Host.Notify(Localization.Format("DoneTCRFormat", modeName, factor, generation.Width, generation.Height), NotificationKind.Success);
        }
        catch (Exception ex) { Host.Notify(Localization.Format("CouldNotReduceTheImageFormat", ex.Message), NotificationKind.Error); }
        finally { _editor.SetProcessingState(false); }
    }
}
