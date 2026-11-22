using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Tessera.Core.Imaging;
using Tessera.Core.Workflows;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

internal sealed partial class EditorTools
{
    private ComboBox channelInput = null!, channelDirectionInput = null!, channelModeInput = null!;

    private TextBox channelAmountInput = null!;

    private TextBlock channelHint = null!;

    private Button channelApplyButton = null!;

    private ChannelAdjustmentOptions _channelAdjustment = new();

    internal void InitializeChannelTool()
    {
        _editor.channelToolGroup.Header = Localization.Text("RGBAChannels");
        var panel = new StackPanel();
        channelInput = Choice("channelInput", ["RRed", "GGreen", "BBlue", "AAlpha"]);
        channelDirectionInput = Choice("channelDirectionInput", ["Add", "Subtract"]);
        channelModeInput = Choice("channelModeInput", ["Value", "OfCurrentValue"]);
        channelAmountInput = new TextBox { Text = "10", VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, MinHeight = 30 };
        _editor.RegisterName("channelAmountInput", channelAmountInput);
        Row("Channel", channelInput);
        Row("Operation", channelDirectionInput);
        Row("ChangeBy", channelModeInput);
        Row("Amount", channelAmountInput);
        channelHint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) };
        channelHint.SetResourceReference(TextBlock.ForegroundProperty, "Ts.TextSecondary");
        _editor.RegisterName("channelHint", channelHint);
        panel.Children.Add(channelHint);
        channelApplyButton = new Button
        {
            Content = Localization.Text("ApplyToChannel"),
            Style = (Style)_editor.FindResource("PrimaryButton"),
            Margin = new Thickness(0, 8, 0, 0),
            IsEnabled = false
        };
        _editor.RegisterName("channelApplyButton", channelApplyButton);
        panel.Children.Add(channelApplyButton);
        _editor.channelToolGroup.Content = panel;
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
            _editor.RegisterName(name, choice);
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
            Channel = (ERgbaChannel)channelInput.SelectedIndex,
            Mode = (EChannelAdjustmentMode)channelModeInput.SelectedIndex,
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
        channelApplyButton.IsEnabled = valid && !_editor._processing && GetFilterBasis() is not null;
        channelHint.Text = !valid ? Localization.Text("EnterANonNegativeNumber")
            : Localization.Text("OnlyTheSelectedChannelChanges") +
                (options.Channel == ERgbaChannel.A ? "\n" + Localization.Text("AddingValueAlsoMakesFullyTransparent") : "");
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
        if (_editor._processing || basis is null || _editor._activeSource is null || !TryReadChannelAdjustment(out var options)) return;
        var document = _editor._activeSource;
        Guid? parentId = ReferenceEquals(basis, _editor._downscaledResult) ? document.SelectedGeneration?.Id : null;
        var settings = CaptureSettings();
        using var input = (Bitmap)basis.Clone();
        string change = options.Channel + " " + (options.Subtract ? "−" : "+") +
            options.Amount.ToString("G", CultureInfo.CurrentCulture) + (options.Mode == EChannelAdjustmentMode.Percent ? "%" : "");
        _editor.SetProcessingState(true);
        Host.statusLabel.Text = Localization.Format("AdjustingChannelAction", options.Channel);
        try
        {
            var generation = await Task.Run(() =>
            {
                using var output = ChannelAdjustment.Apply(input, options, progress: Host.CurrentProcessingProgress);
                return Host._library.SaveGeneration(document, output, null, new GenerationEntry
                {
                    Settings = settings,
                    Operation = Localization.Format("ChannelFormat", change),
                    OperationId = ProcessingOperations.AdjustChannel,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = ProcessingOperations.AdjustChannel, ChannelAdjustment = options }),
                    ParentGenerationId = parentId,
                    PreservesTransparency = true,
                    Caption = Localization.Format("ChannelFormatHeading", change, output.Width, output.Height),
                    Log = Localization.Format("AdjustedChannelFormat", change)
                });
            });
            if (!_editor.History.CommitGeneration(document, generation)) return;
            Host.statusLabel.Text = Localization.Text("ChannelAdjustmentSavedToHistory");
        }
        catch (Exception ex) { Host.statusLabel.Text = Localization.Format("CouldNotAdjustChannelFormat", ex.Message); }
        finally { _editor.SetProcessingState(false); }
    }
}
