using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using PixelArtDownscale;
using PixelArtWorkflows;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class MainWindow
{
    private ComboBox channelInput = null!, channelDirectionInput = null!, channelModeInput = null!;
    private TextBox channelAmountInput = null!;
    private TextBlock channelHint = null!;
    private Button channelApplyButton = null!;
    private ChannelAdjustmentOptions _channelAdjustment = new();

    private void InitializeChannelTool()
    {
        channelToolGroup.Header = Localization.Text("Каналы RGBA");
        var panel = new StackPanel();
        channelInput = Choice("channelInput", ["R — Красный", "G — Зелёный", "B — Синий", "A — Альфа"]);
        channelDirectionInput = Choice("channelDirectionInput", ["Прибавить", "Отнять"]);
        channelModeInput = Choice("channelModeInput", ["Значение", "% от текущего"]);
        channelAmountInput = new TextBox { Text = "10", VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, MinHeight = 30 };
        RegisterName("channelAmountInput", channelAmountInput);
        Row("Канал", channelInput);
        Row("Действие", channelDirectionInput);
        Row("Изменить на", channelModeInput);
        Row("Величина", channelAmountInput);
        channelHint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) };
        channelHint.SetResourceReference(TextBlock.ForegroundProperty, "Ts.TextSecondary");
        RegisterName("channelHint", channelHint);
        panel.Children.Add(channelHint);
        channelApplyButton = new Button
        {
            Content = Localization.Text("Применить к каналу"),
            Style = (Style)FindResource("PrimaryButton"),
            Margin = new Thickness(0, 8, 0, 0),
            IsEnabled = false
        };
        RegisterName("channelApplyButton", channelApplyButton);
        panel.Children.Add(channelApplyButton);
        channelToolGroup.Content = panel;
        foreach (var choice in new[] { channelInput, channelDirectionInput, channelModeInput })
            choice.SelectionChanged += (_, _) => RefreshChannelAdjustmentState();
        channelAmountInput.TextChanged += (_, _) => RefreshChannelAdjustmentState();
        channelAmountInput.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (channelApplyButton.IsEnabled) ApplyChannelAdjustment();
        };
        channelApplyButton.Click += (_, _) => ApplyChannelAdjustment();
        RefreshChannelAdjustmentState();

        ComboBox Choice(string name, string[] values)
        {
            var choice = new ComboBox { MinHeight = 30 };
            foreach (string value in values) choice.Items.Add(Localization.Text(value));
            choice.SelectedIndex = 0;
            RegisterName(name, choice);
            return choice;
        }
        void Row(string label, FrameworkElement control)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = Localization.Text(label), Width = 108, VerticalAlignment = VerticalAlignment.Center });
            AutomationProperties.SetName(control, Localization.Text(label));
            row.Children.Add(control);
            panel.Children.Add(row);
        }
    }

    private bool TryReadChannelAdjustment(out ChannelAdjustmentOptions options)
    {
        options = _channelAdjustment;
        if (channelAmountInput is null || !double.TryParse(channelAmountInput.Text.Trim().Replace(',', '.'),
            NumberStyles.Float, CultureInfo.InvariantCulture, out double amount) || !double.IsFinite(amount) || amount < 0)
            return false;
        if (channelInput.SelectedIndex is < 0 or > 3 || channelModeInput.SelectedIndex is < 0 or > 1 ||
            channelDirectionInput.SelectedIndex is < 0 or > 1) return false;
        options = new ChannelAdjustmentOptions
        {
            Channel = (RgbaChannel)channelInput.SelectedIndex,
            Mode = (ChannelAdjustmentMode)channelModeInput.SelectedIndex,
            Amount = amount,
            Subtract = channelDirectionInput.SelectedIndex == 1
        };
        return true;
    }

    private void RefreshChannelAdjustmentState()
    {
        if (channelApplyButton is null) return;
        bool valid = TryReadChannelAdjustment(out var options);
        if (valid) _channelAdjustment = options;
        channelApplyButton.IsEnabled = valid && !_processing && GetFilterBasis() is not null;
        channelHint.Text = !valid ? Localization.Text("Введите неотрицательное число.")
            : Localization.Text("Меняется только выбранный канал. Значения ограничены диапазоном 0–255. Процент считается от текущего значения каждого пикселя.") +
                (options.Channel == RgbaChannel.A ? "\n" + Localization.Text("Прибавление значения к A делает видимыми и полностью прозрачные пиксели. Процент от нуля остаётся нулём.") : "");
    }

    private void RestoreChannelAdjustment(ChannelAdjustmentOptions? options)
    {
        options ??= new();
        channelInput.SelectedIndex = (int)options.Channel;
        channelDirectionInput.SelectedIndex = options.Subtract ? 1 : 0;
        channelModeInput.SelectedIndex = (int)options.Mode;
        channelAmountInput.Text = options.Amount.ToString("G", CultureInfo.CurrentCulture);
        RefreshChannelAdjustmentState();
    }

    private async void ApplyChannelAdjustment()
    {
        Bitmap? basis = GetFilterBasis();
        if (_processing || basis is null || _activeSource is null || !TryReadChannelAdjustment(out var options)) return;
        var document = _activeSource;
        Guid? parentId = ReferenceEquals(basis, _downscaledResult) ? document.SelectedGeneration?.Id : null;
        var settings = CaptureSettings();
        using var input = (Bitmap)basis.Clone();
        string change = options.Channel + " " + (options.Subtract ? "−" : "+") +
            options.Amount.ToString("G", CultureInfo.CurrentCulture) + (options.Mode == ChannelAdjustmentMode.Percent ? "%" : "");
        SetProcessingState(true);
        statusLabel.Text = Localization.Format("Изменение канала {0}…", options.Channel);
        try
        {
            var generation = await Task.Run(() =>
            {
                using var output = ChannelAdjustment.Apply(input, options, progress: CurrentProcessingProgress);
                return _library.SaveGeneration(document, output, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = Localization.Format("Канал {0}", change),
                    OperationId = ProcessingOperations.AdjustChannel,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = ProcessingOperations.AdjustChannel, ChannelAdjustment = options }),
                    ParentGenerationId = parentId,
                    PreservesTransparency = true,
                    Caption = Localization.Format("КАНАЛ {0} · {1} × {2}", change, output.Width, output.Height),
                    Log = Localization.Format("Изменён канал {0}. Остальные каналы и размер сохранены.", change)
                });
            });
            if (!CommitGeneration(document, generation)) return;
            statusLabel.Text = Localization.Text("Изменение канала сохранено в истории.");
        }
        catch (Exception ex) { statusLabel.Text = Localization.Format("Не удалось изменить канал: {0}", ex.Message); }
        finally { SetProcessingState(false); }
    }
}
