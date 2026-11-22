using System.IO;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;

namespace Tessera.Desktop;

public partial class MainWindow : Window
{
    internal ImageEditorPage Editor { get; }
    internal LibraryPage LibraryPage { get; }

    public int ProcessingBasis { get => Editor.ProcessingBasis; set => Editor.ProcessingBasis = value; }
    public int PreviewZoom { get => Editor.Preview.PreviewZoom; set => Editor.Preview.PreviewZoom = value; }
    public int PreviewBackground { get => Editor.Preview.PreviewBackground; set => Editor.Preview.PreviewBackground = value; }
    public bool AlternativePreview { get => Editor.Preview.AlternativePreview; set => Editor.Preview.AlternativePreview = value; }
    public static readonly DependencyProperty CardWidthProperty = Tessera.Desktop.LibraryPage.CardWidthProperty.AddOwner(typeof(MainWindow));

    public double CardWidth
    {
        get => (double)GetValue(CardWidthProperty);
        set => SetCurrentValue(CardWidthProperty, value);
    }

    private readonly OpenFileDialog openDialog = new() { Multiselect = true };

    public MainWindow() : this(ImageLibrary.DefaultDirectory, AppearanceSettings.DefaultPath) { _autoCheckUpdates = true; }

    public MainWindow(string libraryDirectory, string? appearancePath = null, bool startInWorkspace = false)
    {
        _appearancePath = appearancePath ?? Path.Combine(libraryDirectory, "appearance.json");
        _appearanceSettings = AppearanceSettings.Load(_appearancePath);
        Localization.Initialize(_appearanceSettings.Language);
        openDialog.Filter = Localization.Text("ImagesFileFilter");
        openDialog.Title = Localization.Text("AddImages");
        AppearanceResources.Apply(_appearanceSettings.Theme);
        _library = new ImageLibrary(libraryDirectory);
        InitializeComponent();
        Editor = new ImageEditorPage(this, libraryDirectory);
        LibraryPage = new LibraryPage(this);
        SetBinding(CardWidthProperty, new Binding(nameof(LibraryPage.CardWidth)) { Source = LibraryPage, Mode = BindingMode.TwoWay });
        editorFrame.Content = Editor;
        libraryFrame.Content = LibraryPage;
        editorFrame.SetBinding(VisibilityProperty, new Binding(nameof(Visibility)) { Source = Editor.workspaceHost });
        asepriteWorkspace.ThumbnailCache = _library.Resources;
        InitializeOperationProgress();
        Editor.InitializeTools(libraryDirectory);
        InitializeNavigation();
        Editor.InitializeInputs();
        InitializeWindowFrame();
        Closing += (_, e) =>
        {
            if (e.Cancel) return;
            if (WorkspaceBusy) { e.Cancel = true; Notify(Localization.Text("WaitForProcessingToFinish"), NotificationKind.Warning); }
            else if (!moodboardWorkspace.CanLeave() || !Editor.Atlas.CanCloseAtlas()) e.Cancel = true;
        };
        Closed += (_, _) =>
        {
            Editor.Release();
            _library.Resources.Clear();
        };
        InitializeLibrary(selectLatest: !startInWorkspace);
        Editor.InitializeLiveTools();
        InitializeHome();
        InitializeIcons();
        InitializeAseprite();
        InitializeBackground();
        InitializeMoodboard();
        Editor.InitializePresentation();
        InitializeUpdates();
    }

    internal static void SetVisible(UIElement control, bool visible) => control.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    internal async void OpenImage(object? sender, EventArgs e)
    {
        if (_workspaceReady && _workspaceBrowser?.Visibility == Visibility.Visible) { await PromptWorkspaceFilesAsync(); return; }
        if (Editor._processing || openDialog.ShowDialog(this) != true) return;
        await AddImagesAsync(openDialog.FileNames);
        if (Editor._sourceImage is not null) ShowEditor();
    }

    internal void EnsureExportPathAllowed(string path)
    {
        string output = Path.GetFullPath(path);
        string library = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_library.DirectoryPath));
        if (string.Equals(output, library, StringComparison.OrdinalIgnoreCase) ||
            output.StartsWith(library + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Localization.Text("ChooseFileOutsideInternalTesseraLibrary"));
        var protectedPaths = _sources.Select(source => source.OriginalPath)
            .Concat(backgroundWorkspace.SourcePaths)
            .Concat(iconWorkspace.SourcePaths)
            .Concat(moodboardWorkspace.SourcePaths);
        if (protectedPaths.Any(source => !string.IsNullOrWhiteSpace(source) &&
            string.Equals(output, Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(Localization.Text("CannotOverwriteTheSourceImage"));
    }
}
