using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;

namespace DomainColorTest;

public partial class ReleaseNotesWindow : Window
{
    public ReleaseNotesWindow(ReleaseNotesCatalog catalog, ReleaseNotesState state)
    {
        InitializeComponent();
        Title = notesTitle.Text = Localization.Text("Что нового в Tessera");
        continueButton.Content = Localization.Text("Продолжить");
        AutomationProperties.SetName(closeNotesButton, Localization.Text("Закрыть"));
        closeNotesButton.ToolTip = Localization.Text("Закрыть");
        notesView.Bind(catalog);
        ContentRendered += (_, _) =>
        {
            if (state.MarkSeen(catalog.Current.Version)) return;
            saveWarning.Text = Localization.Text("Не удалось запомнить просмотр. При следующем запуске заметки появятся снова.");
            saveWarning.Visibility = Visibility.Visible;
        };
    }

    private void CloseNotes(object sender, RoutedEventArgs e) => Close();
    private void NotesKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
}
