using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Tessera.Desktop;

public partial class LibraryPage : Page
{
    private readonly MainWindow Host;

    internal LibraryPage(MainWindow host)
    {
        Host = host;
        InitializeComponent();
        homeScroll.SizeChanged += (_, _) => UpdateCardWidth();
        homeAsepriteButton.Click += host.OpenAsepriteWorkspace;
        homeBackgroundButton.Click += host.OpenBackgroundWorkspace;
        homeIconButton.Click += host.OpenIconWorkspace;
        homeMoodboardButton.Click += host.OpenMoodboardWorkspace;
    }

    internal void BindSources(IEnumerable<SourceEntry> sources) => libraryCards.ItemsSource = sources;
    internal void SetEmpty(bool empty) => MainWindow.SetVisible(emptyLibrary, empty);

    internal void ResetScroll()
    {
        homeScroll.ScrollToTop();
        if (SystemParameters.ClientAreaAnimation)
            homeContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0.65, 1, TimeSpan.FromMilliseconds(180)));
        UpdateCardWidth();
    }

    internal void ScrollToLibrary()
    {
        homeScroll.UpdateLayout();
        libraryHeading.BringIntoView();
    }

    internal void ResizeForWindow(double width)
    {
        bool compact = width < 940;
        homeContent.Margin = new Thickness(compact ? 18 : 32, 8, compact ? 18 : 32, 24);
        heroCopy.Margin = new Thickness(compact ? 22 : 32, 28, compact ? 22 : 32, 28);
        heroTitle.FontSize = compact ? 29 : 34;
        iconBannerTitle.FontSize = compact ? 29 : 34;
        backgroundBannerTitle.FontSize = compact ? 29 : 34;
        asepriteBannerTitle.FontSize = compact ? 29 : 34;
        moodboardBannerTitle.FontSize = compact ? 29 : 34;
        iconBannerCopy.Margin = heroCopy.Margin;
        backgroundBannerCopy.Margin = heroCopy.Margin;
        asepriteBannerCopy.Margin = heroCopy.Margin;
        moodboardBannerCopy.Margin = heroCopy.Margin;
        MainWindow.SetVisible(iconBannerArt, width >= 1000);
        MainWindow.SetVisible(backgroundBannerArt, width >= 1100);
        MainWindow.SetVisible(moodboardBannerArt, width >= 1050);
        heroTitle.LineHeight = compact ? 36 : 42;
        UpdateCardWidth();
    }

    public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register(
        nameof(CardWidth), typeof(double), typeof(LibraryPage), new PropertyMetadata(340.0));

    public double CardWidth { get => (double)GetValue(CardWidthProperty); set => SetValue(CardWidthProperty, value); }

    private void HomeOpenImage(object sender, RoutedEventArgs e) => Host.OpenImage(sender, e);

    private void OpenLibraryCard(object sender, RoutedEventArgs e)
    {
        if (Host.Editor._processing || sender is not Button { Tag: SourceEntry source }) return;
        try
        {
            Host.Editor.SelectSource(source);
            Host.ShowEditor();
        }
        catch (Exception ex) { Host.statusLabel.Text = Localization.Format("CouldNotOpenImageFormat", ex.Message); }
    }

    private void UpdateCardWidth()
    {
        double width = homeContent.ActualWidth;
        if (width <= 0) return;
        int columns = width < 650 ? 1 : width < 1050 ? 2 : 3;
        CardWidth = Math.Max(200, (width - 12 * columns) / columns);
    }
}
