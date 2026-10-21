using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;

namespace DomainColorTest;

public partial class ReleaseNotesView : UserControl
{
    private IReadOnlyList<ReleaseNote> _releases = [];

    public ReleaseNotesView()
    {
        InitializeComponent();
        AutomationProperties.SetName(releasePicker, Localization.Text("Версия релиза"));
        AutomationProperties.SetName(notesReader, Localization.Text("Патчноуты"));
    }

    public void Bind(ReleaseNotesCatalog catalog)
    {
        _releases = catalog.Releases;
        releasePicker.ItemsSource = _releases.Select(release => release.Heading).ToArray();
        releasePicker.SelectedIndex = 0;
    }

    private void ReleaseSelected(object sender, SelectionChangedEventArgs e)
    {
        if (releasePicker.SelectedIndex < 0 || releasePicker.SelectedIndex >= _releases.Count) return;
        var release = _releases[releasePicker.SelectedIndex];
        releaseHeading.Text = release.Heading;
        var document = new FlowDocument { PagePadding = new Thickness(0, 0, 12, 0), FontSize = 13, TextAlignment = TextAlignment.Left };
        document.SetResourceReference(FlowDocument.FontFamilyProperty, "Ts.UiFont");
        document.SetResourceReference(FlowDocument.ForegroundProperty, "Ts.Text");
        // Render text only: release descriptions cannot execute HTML or open remote content.
        foreach (string line in release.Body.Replace("\r", "").Split('\n'))
        {
            string text = line.StartsWith("- ") || line.StartsWith("* ") ? "• " + line[2..] : line.TrimStart('#').TrimStart();
            var paragraph = new Paragraph(new Run(text)) { Margin = new Thickness(0, 0, 0, 10), LineHeight = 21 };
            if (line.StartsWith('#')) paragraph.FontWeight = FontWeights.SemiBold;
            document.Blocks.Add(paragraph);
        }
        notesReader.Document = document;
    }
}
