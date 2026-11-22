using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Tessera.Desktop;

public partial class MainWindow
{
    private bool _homeReady;

    private void InitializeHome()
    {
        LibraryPage.BindSources(_sources);
        _sources.CollectionChanged += (_, _) => RefreshHome();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                if (openMenuItem.IsEnabled) openMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, openMenuItem));
            }
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control &&
                (Editor.workspaceHost.Visibility == Visibility.Visible && saveMenuItem.IsEnabled || saveDocumentMenuItem.IsEnabled))
            {
                var command = _workspaceReady || Editor.workspaceHost.Visibility != Visibility.Visible ? saveDocumentMenuItem : saveMenuItem;
                command.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, command));
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
        LibraryPage.SetEmpty(_sources.Count == 0);
    }

    internal bool ShowHome()
    {
        if (!_homeReady || WorkspaceBusy) return false;
        if (_workspaceReady && !moodboardWorkspace.CanLeave()) return false;
        PersistWorkspaceState();
        if (_workspaceReady) ReleaseWorkspaceAsset();
        else Editor.Dock.CloseEditorTools();
        RefreshHome();
        SetVisible(Editor.processButton, false);
        SetVisible(asepriteWorkspace, false);
        SetVisible(iconWorkspace, false);
        SetVisible(backgroundWorkspace, false);
        SetVisible(moodboardWorkspace, false);
        SetVisible(LibraryPage.homeScroll, true);
        SetVisible(Editor.workspaceHost, false);

        LibraryPage.ResetScroll();
        statusLabel.Text = "";
        if (_workspaceReady) { LibraryPage.homeScroll.Visibility = Visibility.Collapsed; ShowWorkspaceBrowser(); }
        UpdateNavigationState();
        return true;
    }

    internal void ShowEditor()
    {
        if (!_homeReady || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy) return;
        if (_workspaceReady && backgroundWorkspace.IsVisible) backgroundWorkspace.ReleaseImage();
        if (_workspaceReady && iconWorkspace.IsVisible) iconWorkspace.ReleaseImage();
        SetVisible(LibraryPage.homeScroll, false);
        SetVisible(asepriteWorkspace, false);
        SetVisible(iconWorkspace, false);
        SetVisible(backgroundWorkspace, false);
        SetVisible(moodboardWorkspace, false);
        SetVisible(Editor.workspaceHost, true);

        SetVisible(Editor.processButton, true);
        HideWorkspaceBrowser();
        UpdateResponsiveLayout();
        UpdateNavigationState();
    }

    private void GoToLibrary(object sender, RoutedEventArgs e)
    {
        ShowHome();
        if (!_workspaceReady) LibraryPage.ScrollToLibrary();
    }

    private void UpdateResponsiveLayout()
    {
        if (!_homeReady || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy) return;
        LibraryPage.ResizeForWindow(ActualWidth);
        Editor.Preview.ResizeForWindow(ActualWidth);
        UpdateNavigationState();
    }
}
