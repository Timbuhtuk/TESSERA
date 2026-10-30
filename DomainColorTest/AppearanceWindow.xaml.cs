using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tessera.WpfStyles.Themes;

namespace DomainColorTest;

public partial class AppearanceWindow : Window
{
    private readonly Func<TesseraTheme, bool> _apply;
    private TesseraTheme _selected;
    private readonly Func<string, bool>? _applyLanguage;
    private readonly Func<int, bool>? _applyUndoDepth;
    private string _language;
    private bool _updating;

    public AppearanceWindow(TesseraTheme theme, Func<TesseraTheme, bool> apply, string language = "ru", Func<string, bool>? applyLanguage = null,
        int undoDepth = 5, Func<int, bool>? applyUndoDepth = null)
    {
        _apply = apply;
        _applyLanguage = applyLanguage;
        _applyUndoDepth = applyUndoDepth;
        _language = language;
        InitializeComponent();
        _updating = true; languageInput.SelectedIndex = language == "en" ? 1 : 0; _updating = false;
        historyUndoHeading.Text = Localization.Text("История отмены");
        historyUndoLabel.Text = Localization.Text("Действий в буфере (1–64)");
        historyUndoDescription.Text = Localization.Text("Ctrl+Z отменяет изменения дерева результатов. Буфер сохраняется между запусками.");
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
        SelectTheme(theme);
        updatesHeading.Text = Localization.Text("Обновления");
        generalTab.Header = Localization.Text("Основные");
        patchNotesTab.Header = Localization.Text("Патчноуты");
        releaseNotesView.Bind(ReleaseNotesCatalog.Bundled);
    }

    public void SetUpdateButton(Button button)
    {
        if (button.Parent is Panel parent) parent.Children.Remove(button);
        button.Visibility = Visibility.Visible;
        button.Margin = new Thickness(0, 0, 0, 8);
        button.Padding = new Thickness(12, 7, 12, 7);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        updatesHost.Children.Add(button);
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
