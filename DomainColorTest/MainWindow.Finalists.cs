using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PixelArtDownscale;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class MainWindow
{
    private readonly GroupBox _finalistGroup = new();
    private readonly RadioButton _t3Mode = new(), _p5Mode = new(), _mixedMode = new();
    private readonly ComboBox _finalistFactor = new();
    private readonly Button _finalistApply = new();

    private void InitializeFinalistTool()
    {
        _finalistGroup.Header = Localization.Text("TCR · уменьшение изображения");
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = Localization.Text("Один пиксель результата на выбранный блок исходных пикселей. Цвет и прозрачность усредняются; тонкие детали могут измениться."),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        AddMode(_t3Mode, "T3 / p44", "Границы ячеек сдвинуты на половину кратности; цвет усредняется с учётом прозрачности.");
        AddMode(_p5Mode, "P5 / OFF", "Обычные блоки выбранного размера, метод R0/C0 без карты регионов.");
        AddMode(_mixedMode, "50 / 50", "Равная смесь T3/p44 и P5/OFF в sRGB с учётом прозрачности.");
        _t3Mode.IsChecked = true;
        var factorRow = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        factorRow.Children.Add(new TextBlock { Text = Localization.Text("Кратность сжатия (2–64)"),
            Width = 132, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        _finalistFactor.MaxDropDownHeight = 240;
        _finalistFactor.ItemsSource = Enumerable.Range(FinalistReducer.MinFactor,
            FinalistReducer.MaxFactor - FinalistReducer.MinFactor + 1);
        _finalistFactor.SelectedItem = FinalistReducer.DefaultFactor;
        AutomationProperties.SetName(_finalistFactor, Localization.Text("Кратность сжатия (2–64)"));
        factorRow.Children.Add(_finalistFactor);
        content.Children.Add(factorRow);
        _finalistApply.Content = Localization.Text("Уменьшить изображение");
        _finalistApply.Style = (Style)FindResource("PrimaryButton");
        _finalistApply.Margin = new Thickness(0, 14, 0, 0);
        _finalistApply.IsEnabled = false;
        AutomationProperties.SetName(_finalistApply, Localization.Text("Применить уменьшение TCR"));
        _finalistApply.Click += (_, _) => ApplyFinalistReduction();
        content.Children.Add(_finalistApply);
        _finalistGroup.Content = content;
        settingsPanel.Children.Add(_finalistGroup);

        void AddMode(RadioButton button, string title, string explanation)
        {
            button.GroupName = "TcrFinalistMode";
            button.Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = Localization.Text(explanation), TextWrapping = TextWrapping.Wrap, MaxWidth = 210,
                        Foreground = (System.Windows.Media.Brush)FindResource("Ts.TextSecondary") }
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
        if (_processing || basis is null || _activeSource is null) return;
        if (_finalistFactor.SelectedItem is not int factor || factor is < FinalistReducer.MinFactor or > FinalistReducer.MaxFactor)
        {
            statusLabel.Text = Localization.Text("Укажите целую кратность сжатия от 2 до 64.");
            return;
        }
        var document = _activeSource;
        Guid? parentId = ReferenceEquals(basis, _downscaledResult) ? document.SelectedGeneration?.Id : null;
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
        SetProcessingState(true);
        statusLabel.Text = Localization.Format("Уменьшение TCR {0}×…", factor);
        try
        {
            var generation = await Task.Run(() =>
            {
                using var output = FinalistReducer.Reduce(input, mode, factor);
                return _library.SaveGeneration(document, output, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = $"TCR {modeName} {factor}×",
                    OperationId = ProcessingOperations.FinalistReduce,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = ProcessingOperations.FinalistReduce,
                        FinalistMode = mode, FinalistFactor = factor }),
                    ParentGenerationId = parentId,
                    PreservesTransparency = true,
                    Caption = $"TCR {modeName} {factor}× · {output.Width} × {output.Height}",
                    Log = Localization.Format("TCR {0}: {1} × {2} → {3} × {4}; ячейка {5}×, прозрачность 0–255.",
                        modeName, input.Width, input.Height, output.Width, output.Height, factor)
                });
            });
            document.Generations.Add(generation);
            SelectGeneration(generation);
            statusLabel.Text = Localization.Format("Готово · TCR {0} {1}×, {2} × {3}.", modeName, factor, generation.Width, generation.Height);
        }
        catch (Exception ex) { statusLabel.Text = Localization.Format("Не удалось уменьшить изображение: {0}", ex.Message); }
        finally { SetProcessingState(false); }
    }
}
