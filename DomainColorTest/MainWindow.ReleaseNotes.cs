using System.IO;
using System.Windows;

namespace DomainColorTest;

public partial class MainWindow
{
    private bool _releaseNotesEnabled;
    private ReleaseNotesState NotesState => new(Path.Combine(Path.GetDirectoryName(_appearancePath)!, "release-notes-state.json"));

    public void EnableReleaseNotes()
    {
        if (_releaseNotesEnabled) return;
        _releaseNotesEnabled = true;
        ReleaseNotesStartup.Attach(this, NotesState);
    }
}

public static class ReleaseNotesStartup
{
    public static void Attach(Window owner, ReleaseNotesState state)
    {
        owner.ContentRendered += ShowNotes;
        void ShowNotes(object? sender, EventArgs e)
        {
            owner.ContentRendered -= ShowNotes;
            var catalog = ReleaseNotesCatalog.Bundled;
            if (state.HasSeen(catalog.Current.Version)) return;
            var notes = new ReleaseNotesWindow(catalog, state) { Owner = owner, ShowActivated = owner.ShowActivated, Opacity = owner.Opacity };
            notes.Show();
        }
    }
}
