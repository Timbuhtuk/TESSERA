using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DomainColorTest;

internal sealed class MacroNameDialog : Window
{
    internal TextBox NameInput { get; } = new() { Margin = new Thickness(0, 8, 0, 6) };
    internal Button Confirm { get; } = new() { Content = Localization.Text("Готово"), IsDefault = true };
    public string MacroName => NameInput.Text.Trim();

    public MacroNameDialog(string title, string initial, Func<string, bool> isUnique)
    {
        Title = title; Width = 420; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Resources.MergedDictionaries.Add(new AppearanceResources());
        SetResourceReference(FontFamilyProperty, "UiFont"); SetResourceReference(ForegroundProperty, "Ts.Text");
        var panel = new StackPanel();
        var heading = new TextBlock { Text = title, FontSize = 20, Margin = new Thickness(0, 0, 0, 14) };
        heading.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        panel.Children.Add(heading); panel.Children.Add(new TextBlock { Text = Localization.Text("Имя макроса") });
        NameInput.Text = initial; panel.Children.Add(NameInput);
        var validation = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        validation.SetResourceReference(ForegroundProperty, "Ts.TextSecondary"); panel.Children.Add(validation);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = Localization.Text("Отмена"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Style = (Style)FindResource("Ts.CompactButton"); Confirm.Style = (Style)FindResource("Ts.PrimaryButton");
        buttons.Children.Add(cancel); buttons.Children.Add(Confirm); panel.Children.Add(buttons);
        Content = new Border { Style = (Style)FindResource("Ts.DialogBorder"), Margin = new Thickness(14), Child = panel };
        void Validate()
        {
            string name = MacroName;
            validation.Text = name.Length == 0 ? Localization.Text("Введите имя макроса.") : !isUnique(name) ? Localization.Text("Макрос с таким именем уже существует.") : "";
            Confirm.IsEnabled = validation.Text.Length == 0;
        }
        NameInput.TextChanged += (_, _) => Validate();
        Confirm.Click += (_, _) => { if (Confirm.IsEnabled) DialogResult = true; };
        Loaded += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); };
        Validate();
    }
}
