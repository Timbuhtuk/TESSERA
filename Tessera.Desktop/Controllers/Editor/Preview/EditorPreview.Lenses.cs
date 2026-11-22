using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Tessera.Desktop;

internal sealed partial class EditorPreview
{
    private string? _comparisonLens;

    private bool IsLensOpen(string tool) => _editor.Dock.IsToolOpen(tool);

    private string? ComparisonLens(Key key) =>
        AppearanceSettings.Lenses.FirstOrDefault(lens => Host._appearanceSettings.LensKey(lens.Tool) == key).Tool ??
        AppearanceSettings.Lenses.FirstOrDefault(lens => IsLensOpen(lens.Tool)).Tool;

    internal bool IsLensVisible(string tool, bool enabled) =>
        !AlternativePreview && (_comparisonLens is null || _comparisonLens == tool) && (enabled != (_comparisonLens == tool));

    private void SetComparisonLens(string? lens)
    {
        if (_comparisonLens == lens) return;
        bool updateContours = IsContourToolOpen || _comparisonLens == "contours" || lens == "contours";
        _comparisonLens = lens;
        UpdateGridLens();
        UpdateTileHeatmapLens();
        if (updateContours) UpdateContourComparison();
    }

    internal void CloseLens(string tool) => _editor.Dock.CloseTool(tool);

    internal void UpdateLensKeyHints()
    {
        foreach (var lens in AppearanceSettings.Lenses)
            Host.SetToolGesture(lens.Tool, AppearanceSettings.ComparisonKeyLabel(Host._appearanceSettings.LensKey(lens.Tool)));
    }

    private static StackPanel LensSlider(string name, string format, Slider slider)
    {
        var label = new TextBlock { Text = Localization.Format(format, slider.Value), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(slider, Localization.Text(name));
        slider.ValueChanged += (_, e) => label.Text = Localization.Format(format, e.NewValue);
        return new StackPanel { Margin = new Thickness(0, 0, 0, 8), Children = { label, slider } };
    }

    internal void LensKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Host._appearanceSettings.ComparisonPreviewKey || e.IsRepeat || Keyboard.Modifiers != ModifierKeys.None || !_editor.ImageEditorVisible ||
            Host.WorkspaceBusy || Host.mainMenu.IsKeyboardFocusWithin ||
            Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox ||
            e.Key == Key.Space && Keyboard.FocusedElement is ButtonBase) return;
        foreach (var lens in AppearanceSettings.Lenses)
        {
            if (e.Key != Host._appearanceSettings.LensKey(lens.Tool)) continue;
            e.Handled = true;
            if (IsLensOpen(lens.Tool)) CloseLens(lens.Tool);
            else
            {
                _editor.Dock.OpenRegisteredTool(lens.Tool);
            }
            return;
        }
    }
}
