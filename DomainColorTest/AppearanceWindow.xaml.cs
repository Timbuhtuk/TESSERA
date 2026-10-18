using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tessera.WpfStyles.Themes;

namespace DomainColorTest;

public partial class AppearanceWindow : Window
{
    private readonly Func<TesseraTheme, bool> _apply;
    private TesseraTheme _selected;
    private bool _updating;

    public AppearanceWindow(TesseraTheme theme, Func<TesseraTheme, bool> apply)
    {
        _apply = apply;
        InitializeComponent();
        SelectTheme(theme);
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
