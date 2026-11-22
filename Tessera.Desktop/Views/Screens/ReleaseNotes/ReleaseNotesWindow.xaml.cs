using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;

namespace Tessera.Desktop;

public partial class ReleaseNotesWindow : Window
{
    public ReleaseNotesWindow(ReleaseNotesCatalog catalog, ReleaseNotesState state)
    {
        InitializeComponent();
        Title = notesTitle.Text = Localization.Text("WhatSNewInTessera");
        continueButton.Content = Localization.Text("Continue");
        AutomationProperties.SetName(closeNotesButton, Localization.Text("Close"));
        closeNotesButton.ToolTip = Localization.Text("Close");
        notesView.Bind(catalog);
        ContentRendered += (_, _) =>
        {
            if (state.MarkSeen(catalog.Current.Version)) return;
            saveWarning.Text = Localization.Text("CouldNotSaveThatTheseNotes");
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
