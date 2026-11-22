using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Tessera.WpfStyles.Themes;

namespace Tessera.Desktop;

public partial class AppearanceWindow : Window
{
    private readonly Func<TesseraTheme, bool> _apply;
    private TesseraTheme _selected;
    private readonly Func<string, bool>? _applyLanguage;
    private readonly Func<int, bool>? _applyUndoDepth;
    private sealed record ShortcutInput(Button Input, Button Reset, string Label, Key DefaultKey,
        Func<Key> Read, Action<Key>? Apply);
    private readonly List<ShortcutInput> _shortcutInputs = [];
    private ShortcutInput? _capturingShortcut;
    private string _language;
    private bool _updating;
    private NotificationPlacement _notificationPosition;

    public event Action? UninstallRequested;

    public AppearanceWindow(TesseraTheme theme, Func<TesseraTheme, bool> apply, string language = "ru", Func<string, bool>? applyLanguage = null,
        int undoDepth = 5, Func<int, bool>? applyUndoDepth = null, Key comparisonKey = Key.Space, Func<Key, bool>? applyComparisonKey = null)
    {
        _apply = apply;
        _applyLanguage = applyLanguage;
        _applyUndoDepth = applyUndoDepth;
        _language = language;
        InitializeComponent();
        _updating = true; languageInput.SelectedIndex = language == "en" ? 1 : 0; _updating = false;
        historyUndoHeading.Text = Localization.Text("UndoHistory");
        historyUndoLabel.Text = Localization.Text("ActionsInBuffer164");
        historyUndoDescription.Text = Localization.Text("CtrlZUndoesResultTreeChanges");
        historyUndoDepthInput.Value = Math.Clamp(undoDepth, 1, 64);
        historyUndoDepthInput.ValueChanged += (_, _) =>
        {
            if (_updating || _applyUndoDepth is null) return;
            if (!_applyUndoDepth(historyUndoDepthInput.Value))
            {
                _updating = true; historyUndoDepthInput.Value = undoDepth; _updating = false;
            }
            else undoDepth = historyUndoDepthInput.Value;
        };
        languageSavedHint.Visibility = language == Localization.Language ? Visibility.Collapsed : Visibility.Visible;
        comparisonKeyLabel.Text = Localization.Text("AlternativePreviewKey");
        comparisonKeyInput.ToolTip = Localization.Text("HoldKeyShowSourceAlternativePreview");
        AddShortcutInput(new(comparisonKeyInput, comparisonKeyReset, comparisonKeyLabel.Text, Key.Space,
            () => comparisonKey, applyComparisonKey is null ? null : key =>
            {
                if (key != comparisonKey && applyComparisonKey(key)) comparisonKey = key;
            }));
        Deactivated += (_, _) => StopCapturingShortcut();
        SelectTheme(theme);
        updatesHeading.Text = Localization.Text("Updates");
        uninstallButton.Content = Localization.Text("UninstallTesseraAction");
        installedVersionLabel.Text = Localization.Format("CurrentVersionFormat", $"v{GitHubUpdater.CurrentVersion.ToString(3)}");
        generalTab.Header = Localization.Text("General");
        patchNotesTab.Header = Localization.Text("PatchNotes");
        releaseNotesView.Bind(ReleaseNotesCatalog.Bundled);
    }

    private void UninstallClicked(object sender, RoutedEventArgs e) => UninstallRequested?.Invoke();

    internal void ConfigureNotifications(NotificationPlacement position, Func<NotificationPlacement, bool> apply)
    {
        var choices = new (NotificationPlacement? Position, string Mark, string Label)[]
        {
            (NotificationPlacement.TopLeft, "↖", "TopLeft"),
            (NotificationPlacement.TopCenter, "↑", "TopCenter"),
            (NotificationPlacement.TopRight, "↗", "TopRight"),
            (NotificationPlacement.MiddleLeft, "←", "CenterLeft"),
            (null, "", ""),
            (NotificationPlacement.MiddleRight, "→", "CenterRight"),
            (NotificationPlacement.BottomLeft, "↙", "BottomLeft"),
            (NotificationPlacement.BottomCenter, "↓", "BottomCenter"),
            (NotificationPlacement.BottomRight, "↘", "BottomRight")
        };
        _notificationPosition = position;
        notificationPositionLabel.Text = Localization.Text("NotificationPosition");
        AutomationProperties.SetName(notificationPositions, notificationPositionLabel.Text);
        notificationPreviewButton.Content = Localization.Text("ShowANotification");
        notificationPreview.Placement = position;
        RadioButton? selected = null;
        foreach (var choice in choices)
        {
            if (choice.Position is not { } anchor)
            {
                notificationPositions.Children.Add(new Border());
                continue;
            }

            string label = Localization.Text(choice.Label);
            var button = new RadioButton
            {
                GroupName = "NotificationPosition",
                Content = new TextBlock { Text = choice.Mark, FontSize = 16 },
                ToolTip = label,
                Style = (Style)FindResource("NotificationPositionChoice"),
                IsChecked = anchor == position
            };
            AutomationProperties.SetName(button, label);
            if (anchor == position)
            {
                selected = button;
                notificationPositionValue.Text = label;
            }
            button.Checked += (_, _) =>
            {
                if (anchor == _notificationPosition) return;
                if (!apply(anchor))
                {
                    if (selected is not null) selected.IsChecked = true;
                    return;
                }

                selected = button;
                _notificationPosition = anchor;
                notificationPositionValue.Text = label;
                notificationPreview.Placement = anchor;
            };
            notificationPositions.Children.Add(button);
        }
        settingsTabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == settingsTabs) notificationPreview.Clear();
        };
        notificationSettings.Visibility = Visibility.Visible;
    }

    private void PreviewNotification(object sender, RoutedEventArgs e)
    {
        notificationPreview.Show(Localization.Text("NotificationsWillAppearThisPartWindow"), heading: Localization.Text("Notification"));
    }

    internal void AddLensShortcuts(AppearanceSettings settings, Func<string, Key, AppearanceSettings?> apply)
    {
        lensShortcuts.Children.Add(new TextBlock
        {
            Text = Localization.Text("Lenses"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8)
        });
        foreach (var lens in AppearanceSettings.Lenses)
        {
            string title = Localization.Text(lens.TitleKey).TrimEnd('…');
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var reset = new Button { Name = lens.Tool + "KeyReset" };
            var input = new Button { Name = lens.Tool + "KeyInput", MinWidth = 130, Margin = new Thickness(8, 0, 8, 0) };
            DockPanel.SetDock(reset, Dock.Right);
            DockPanel.SetDock(input, Dock.Right);
            row.Children.Add(reset);
            row.Children.Add(input);
            row.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            lensShortcuts.Children.Add(row);
            RegisterName(reset.Name, reset);
            RegisterName(input.Name, input);
            AddShortcutInput(new(input, reset, title, lens.DefaultKey, () => settings.LensKey(lens.Tool), key =>
            {
                if (key != settings.LensKey(lens.Tool) && apply(lens.Tool, key) is { } saved) settings = saved;
            }));
        }
    }

    private void AddShortcutInput(ShortcutInput shortcut)
    {
        _shortcutInputs.Add(shortcut);
        shortcut.Input.IsEnabled = shortcut.Apply is not null;
        shortcut.Reset.Content = Localization.Text("Reset");
        AutomationProperties.SetName(shortcut.Reset, shortcut.Label + " · " + shortcut.Reset.Content);
        shortcut.Input.Click += (_, _) =>
        {
            shortcut.Input.Focus();
            _capturingShortcut = shortcut;
            UpdateShortcutInputs();
        };
        shortcut.Input.PreviewKeyDown += (_, e) => CaptureShortcutKeyDown(shortcut, e);
        shortcut.Input.LostKeyboardFocus += (_, _) =>
        {
            if (_capturingShortcut == shortcut) StopCapturingShortcut();
        };
        shortcut.Reset.Click += (_, _) =>
        {
            shortcut.Apply?.Invoke(shortcut.DefaultKey);
            StopCapturingShortcut();
        };
        UpdateShortcutInputs();
    }

    private void CaptureShortcutKeyDown(ShortcutInput shortcut, KeyEventArgs e)
    {
        if (_capturingShortcut != shortcut || e.Key == Key.Tab || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        if (e.Key == Key.Escape) StopCapturingShortcut();
        else if (!e.IsRepeat && AppearanceSettings.CanBindComparisonKey(e.Key))
        {
            shortcut.Apply?.Invoke(e.Key);
            StopCapturingShortcut();
        }
    }

    private void StopCapturingShortcut()
    {
        _capturingShortcut = null;
        UpdateShortcutInputs();
    }

    private void UpdateShortcutInputs()
    {
        foreach (var shortcut in _shortcutInputs)
        {
            var key = shortcut.Read();
            string label = _capturingShortcut == shortcut
                ? Localization.Text("PressAKeyAction") : AppearanceSettings.ComparisonKeyLabel(key);
            shortcut.Input.Content = label;
            AutomationProperties.SetName(shortcut.Input, shortcut.Label + " · " + label);
            shortcut.Reset.IsEnabled = shortcut.Apply is not null && key != shortcut.DefaultKey;
        }
    }

    public void SetUpdateButton(Button button)
    {
        if (button.Parent is Panel parent) parent.Children.Remove(button);
        button.Visibility = Visibility.Visible;
        button.Margin = new Thickness(0, 0, 12, 8);
        button.Padding = new Thickness(12, 7, 12, 7);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        updatesHost.Children.Insert(0, button);
        RegisterName("updateButton", button);
        Closed += (_, _) => { updatesHost.Children.Remove(button); UnregisterName("updateButton"); };
    }

    private void LanguageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || languageInput.SelectedItem is not ComboBoxItem { Tag: string language } || language == _language) return;
        if (_applyLanguage?.Invoke(language) == true)
        {
            _language = language;
            languageSavedHint.Visibility = language == Localization.Language ? Visibility.Collapsed : Visibility.Visible;
        }
        else { _updating = true; languageInput.SelectedIndex = _language == "en" ? 1 : 0; _updating = false; }
    }

    private void SelectTheme(TesseraTheme theme)
    {
        _updating = true;
        _selected = theme;
        lightTheme.IsChecked = theme == TesseraTheme.Light;
        darkTheme.IsChecked = theme == TesseraTheme.Dark;
        contrastTheme.IsChecked = theme == TesseraTheme.Contrast;
        _updating = false;
    }

    private void ThemeSelected(object sender, RoutedEventArgs e)
    {
        if (_updating || sender is not RadioButton { Tag: string tag }) return;
        var theme = Enum.Parse<TesseraTheme>(tag);
        if (theme == _selected) return;
        if (_apply(theme)) _selected = theme;
        else SelectTheme(_selected);
    }

    private void CloseAppearance(object sender, RoutedEventArgs e) => Close();

    private void AppearanceKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
}
