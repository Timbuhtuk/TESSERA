using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DomainColorTest;

public partial class TesseraDialog : Window
{
    private readonly MessageBoxButton _buttons;

    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    public TesseraDialog(string message, string title, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        InitializeComponent();
        _buttons = buttons;
        Title = title;
        dialogTitle.Text = title;
        dialogMessage.Text = message;
        MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 40);
        BuildButtons(buttons);
    }

    public static MessageBoxResult Show(Window? owner, string message, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = new TesseraDialog(message, title, buttons, image);
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        dialog.ShowDialog();
        return dialog.Result;
    }

    public static MessageBoxResult Show(string message, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None) =>
        Show(Application.Current?.MainWindow, message, title, buttons, image);

    private void BuildButtons(MessageBoxButton buttons)
    {
        if (buttons is MessageBoxButton.OK or MessageBoxButton.OKCancel) AddButton("ОК", MessageBoxResult.OK, true, false);
        if (buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel) AddButton("Да", MessageBoxResult.Yes, true, false);
        if (buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel) AddButton("Нет", MessageBoxResult.No, false, buttons == MessageBoxButton.YesNo);
        if (buttons is MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel) AddButton("Отмена", MessageBoxResult.Cancel, false, true);
    }

    private void AddButton(string label, MessageBoxResult result, bool primary, bool cancel)
    {
        var button = new Button
        {
            Content = label,
            Tag = result,
            MinWidth = 88,
            Margin = dialogButtons.Children.Count == 0 ? new Thickness(0) : new Thickness(8, 0, 0, 0),
            IsDefault = primary,
            IsCancel = cancel,
            Style = (Style)FindResource(primary ? "Ts.PrimaryButton" : "Ts.CompactButton")
        };
        button.Click += Choose;
        dialogButtons.Children.Add(button);
    }

    private void Choose(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MessageBoxResult result }) Result = result;
        Close();
    }

    private void CloseDialog(object sender, RoutedEventArgs e)
    {
        Result = _buttons switch
        {
            MessageBoxButton.OK => MessageBoxResult.OK,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            _ => MessageBoxResult.Cancel
        };
        Close();
    }

    private void DialogKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CloseDialog(sender, e);
        e.Handled = true;
    }

    private void DragDialog(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}