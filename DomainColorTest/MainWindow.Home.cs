using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace DomainColorTest;

public partial class MainWindow
{
    public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register(
        nameof(CardWidth), typeof(double), typeof(MainWindow), new PropertyMetadata(340.0));
    public double CardWidth { get => (double)GetValue(CardWidthProperty); set => SetValue(CardWidthProperty, value); }
    private bool _homeReady;

    private void InitializeHome()
    {
        libraryCards.ItemsSource = _sources;
        _sources.CollectionChanged += (_, _) => RefreshHome();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        homeScroll.SizeChanged += (_, _) => UpdateCardWidth();
        workspaceHost.SizeChanged += (_, _) => UpdatePreviewHeight();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (asepriteWorkspace.Visibility == Visibility.Visible) _ = asepriteWorkspace.OpenAsync();
                else if (iconWorkspace.Visibility == Visibility.Visible) _ = iconWorkspace.OpenAsync();
                else if (backgroundWorkspace.Visibility == Visibility.Visible) _ = backgroundWorkspace.OpenAsync();
                else OpenImage(this, EventArgs.Empty);
                e.Handled = true;
            }
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control &&
                workspaceHost.Visibility == Visibility.Visible && saveButton.IsEnabled)
            {
                SaveImage(this, EventArgs.Empty);
                e.Handled = true;
            }
        };
        _homeReady = true;
        ShowHome();
        Loaded += (_, _) => UpdateResponsiveLayout();
    }

    private void RefreshHome()
    {
        if (!_homeReady || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy) return;
        SetVisible(emptyLibrary, _sources.Count == 0);
    }

    private void ShowHome()
    {
        if (!_homeReady || _processing || iconWorkspace.IsBusy || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy) return;
        CloseEditorTools();
        RefreshHome();
        SetVisible(processButton, false);
        SetVisible(asepriteWorkspace, false);
        SetVisible(iconWorkspace, false);
        SetVisible(backgroundWorkspace, false);
        SetVisible(homeScroll, true);
        SetVisible(workspaceHost, false);
        SetVisible(editorToolbar, false);
        homeScroll.ScrollToTop();
        statusLabel.Text = "Готово";
        if (SystemParameters.ClientAreaAnimation)
            homeContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0.65, 1, TimeSpan.FromMilliseconds(180)));
        UpdateCardWidth();
    }

    private void ShowEditor()
    {
        if (!_homeReady || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy) return;
        SetVisible(homeScroll, false);
        SetVisible(asepriteWorkspace, false);
        SetVisible(iconWorkspace, false);
        SetVisible(backgroundWorkspace, false);
        SetVisible(workspaceHost, true);
        SetVisible(editorToolbar, true);
        SetVisible(processButton, true);
        UpdateResponsiveLayout();
    }
    private void HomeOpenImage(object sender, RoutedEventArgs e) => OpenImage(sender, e);

    private void GoToLibrary(object sender, RoutedEventArgs e)
    {
        ShowHome();
        homeScroll.UpdateLayout();
        libraryHeading.BringIntoView();
    }

    private void OpenLibraryCard(object sender, RoutedEventArgs e)
    {
        if (_processing || sender is not Button { Tag: SourceEntry source }) return;
        try
        {
            SelectSource(source);
            ShowEditor();
        }
        catch (Exception ex) { statusLabel.Text = $"Не удалось открыть изображение: {ex.Message}"; }
    }

    private void UpdateCardWidth()
    {
        double width = homeContent.ActualWidth;
        if (width <= 0) return;
        int columns = width < 650 ? 1 : width < 1050 ? 2 : 3;
        CardWidth = Math.Max(200, (width - 12 * columns) / columns);
    }

    private void UpdateResponsiveLayout()
    {
        if (!_homeReady || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy) return;
        bool compact = ActualWidth < 940;
        homeContent.Margin = new Thickness(compact ? 18 : 32, 8, compact ? 18 : 32, 24);
        heroCopy.Margin = new Thickness(compact ? 22 : 32, 28, compact ? 22 : 32, 28);
        heroTitle.FontSize = compact ? 29 : 34;
        iconBannerTitle.FontSize = compact ? 29 : 34;
        backgroundBannerTitle.FontSize = compact ? 29 : 34;
        asepriteBannerTitle.FontSize = compact ? 29 : 34;
        iconBannerCopy.Margin = heroCopy.Margin;
        backgroundBannerCopy.Margin = heroCopy.Margin;
        asepriteBannerCopy.Margin = heroCopy.Margin;
        SetVisible(asepriteBannerArt, ActualWidth >= 1100);
        SetVisible(iconBannerArt, ActualWidth >= 1000);
        SetVisible(backgroundBannerArt, ActualWidth >= 1100);
        heroTitle.LineHeight = compact ? 36 : 42;
        previewArea.Margin = compact ? new Thickness(12, 0, 12, 10) : new Thickness(18, 0, 18, 12);
        workspaceHost.VerticalScrollBarVisibility = compact ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        UpdatePreviewOrientation();
        UpdatePreviewHeight();
        UpdateCardWidth();
    }

    private void UpdatePreviewOrientation()
    {
        bool wide = ActualWidth >= 1200;
        if ((Grid.GetColumn(sourcePane) == 2) == wide) return;

        if (wide)
        {
            previewArea.Children.Remove(sourceTray);
            historyRow.Children.Add(sourceTray);
            Grid.SetRow(sourceTray, 0);
            Grid.SetColumn(sourceTray, 2);
            Grid.SetColumnSpan(resultTray, 1);
            sourceTray.Margin = new Thickness(0);
            sourceTray.Padding = new Thickness(0);
            sourceTray.BorderThickness = new Thickness(0);
        }
        else
        {
            historyRow.Children.Remove(sourceTray);
            previewArea.Children.Add(sourceTray);
            Grid.SetRow(sourceTray, 2);
            Grid.SetColumn(sourceTray, 0);
            Grid.SetColumnSpan(resultTray, 3);
            sourceTray.Margin = new Thickness(0, 8, 0, 0);
            sourceTray.Padding = new Thickness(0, 5, 0, 0);
            sourceTray.BorderThickness = new Thickness(0, 1, 0, 0);
        }

        previewPair.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
        previewPair.RowDefinitions[1].Height = new GridLength(wide ? 0 : 6);
        previewPair.RowDefinitions[2].Height = wide ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        previewPair.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        previewPair.ColumnDefinitions[1].Width = new GridLength(wide ? 6 : 0);
        previewPair.ColumnDefinitions[2].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetRow(sourcePane, wide ? 0 : 2);
        Grid.SetColumn(sourcePane, wide ? 2 : 0);
        Grid.SetRow(previewSplitter, wide ? 0 : 1);
        Grid.SetColumn(previewSplitter, wide ? 1 : 0);
        previewSplitter.ResizeDirection = wide ? GridResizeDirection.Columns : GridResizeDirection.Rows;
        previewSplitter.Width = wide ? 6 : double.NaN;
        previewSplitter.Height = wide ? double.NaN : 6;
        previewSplitter.HorizontalAlignment = wide ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        previewSplitter.VerticalAlignment = wide ? VerticalAlignment.Stretch : VerticalAlignment.Center;
    }

    private void UpdatePreviewHeight()
    {
        double minimum = ActualWidth < 940 ? 700 : 0;
        workspaceGrid.MinHeight = Math.Max(minimum, workspaceHost.ActualHeight);
    }

    private void HomeScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (landscapeOffset is null || !SystemParameters.ClientAreaAnimation) return;
        landscapeOffset.Y = Math.Min(12, homeScroll.VerticalOffset * 0.035);
    }

    private void HeroMouseMove(object sender, MouseEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var position = e.GetPosition(heroPanel);
        landscapeOffset.X = (position.X / Math.Max(1, heroPanel.ActualWidth) - 0.5) * 10;
    }

    private void HeroMouseLeave(object sender, MouseEventArgs e) => landscapeOffset.X = 0;
}
