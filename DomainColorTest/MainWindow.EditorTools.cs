using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Shell;

namespace DomainColorTest;

public partial class MainWindow
{
    private readonly Dictionary<string, EditorToolWindow> _toolWindows = [];

    private void InitializeEditorTools()
    {
        profileToolButton.Click += (_, _) => OpenEditorTool("profile", "Готовые режимы", 320, profileToolGroup, executionToolGroup);
        sizeToolButton.Click += (_, _) => OpenEditorTool("size", "Размер", 370, sizeToolGroup, frameToolGroup, pixelToolGroup);
        colorToolButton.Click += (_, _) => OpenEditorTool("color", "Палитра", 440, colorToolGroup);
        smoothingToolButton.Click += (_, _) => OpenEditorTool("smoothing", "Сглаживание", 470, smoothingToolGroup);
        gridToolButton.Click += (_, _) => OpenEditorTool("grid", "Сетка", 280, gridToolGroup);
        infoToolButton.Click += (_, _) => { OpenEditorTool("info", "Инфо", 560, infoToolGroup); RefreshInfo(); };
        fileMenuButton.Click += (_, _) => fileMenuPopup.IsOpen = !fileMenuPopup.IsOpen;
        toolsMenuButton.Click += (_, _) => toolsMenuPopup.IsOpen = !toolsMenuPopup.IsOpen;
        foreach (var pair in new[]
        {
            (compactProfileToolButton, profileToolButton),
            (compactSizeToolButton, sizeToolButton),
            (compactColorToolButton, colorToolButton),
            (compactSmoothingToolButton, smoothingToolButton),
            (compactGridToolButton, gridToolButton),
            (compactInfoToolButton, infoToolButton)
        })
            pair.Item1.Click += (_, _) => { toolsMenuPopup.IsOpen = false; pair.Item2.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
        SizeChanged += (_, _) => UpdateToolNavigation();
        UpdateToolNavigation();
    }

    private void UpdateToolNavigation()
    {
        bool compact = (ActualWidth > 0 ? ActualWidth : Width) < 1500;
        SetVisible(toolNavigation, !compact);
        SetVisible(compactToolNavigation, compact);
        if (!compact) toolsMenuPopup.IsOpen = false;
    }

    private void OpenEditorTool(string key, string title, double height, params GroupBox[] groups)
    {
        if (_toolWindows.TryGetValue(key, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        var content = new StackPanel { Margin = new Thickness(16, 16, 16, 4), IsEnabled = !_processing };
        StackPanel? advancedContent = groups.Length > 1 ? new StackPanel() : null;
        for (int index = 0; index < groups.Length; index++)
        {
            var group = groups[index];
            if (group.Parent is not Panel parent) throw new InvalidOperationException("Панель инструмента уже используется.");
            // Сохраняем стили при переносе: иначе Slider временно получает Maximum = 10 и меняет Value.
            if (!group.Resources.MergedDictionaries.Contains(Resources)) group.Resources.MergedDictionaries.Add(Resources);
            parent.Children.Remove(group);
            if (index == 0) content.Children.Add(group);
            else advancedContent!.Children.Add(group);
        }
        if (advancedContent is not null)
            content.Children.Add(new Expander
            {
                Header = "Дополнительные настройки",
                Content = advancedContent,
                Style = (Style)FindResource("ToolExpander"),
                Margin = new Thickness(0, 2, 0, 8)
            });

        var tool = new EditorToolWindow(this, title, content, height);
        tool.Closed += (_, _) =>
        {
            _toolWindows.Remove(key);
            if (key == "info") ResetInfoSnapshot();
            foreach (var group in groups)
            {
                if (group.Parent is Panel parent) parent.Children.Remove(group);
                settingsPanel.Children.Add(group);
            }
        };
        _toolWindows.Add(key, tool);
        tool.Show();
    }

    private void CloseEditorTools()
    {
        foreach (var tool in _toolWindows.Values.ToArray()) tool.Close();
        fileMenuPopup.IsOpen = false;
        toolsMenuPopup.IsOpen = false;
    }

    private sealed class EditorToolWindow : Window
    {
        public StackPanel ToolContent { get; }

        public EditorToolWindow(MainWindow owner, string title, StackPanel content, double height)
        {
            Owner = owner;
            Title = title + " · TESSERA";
            Icon = owner.Icon;
            Width = 440;
            Height = Math.Min(height, SystemParameters.WorkArea.Height - 60);
            MinWidth = 360;
            MinHeight = 230;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(16, 16, 16));
            Foreground = new SolidColorBrush(Color.FromRgb(238, 238, 238));
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Resources.MergedDictionaries.Add(owner.Resources);
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 38,
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });

            PreviewKeyDown += (_, e) =>
            {
                if (Keyboard.Modifiers != ModifierKeys.Control) return;
                if (e.Key == Key.O) owner.OpenImage(this, EventArgs.Empty);
                else if (e.Key == Key.S && owner.saveButton.IsEnabled) owner.SaveImage(this, EventArgs.Empty);
                else return;
                e.Handled = true;
            };

            ToolContent = content;
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition());

            var titleBar = new Grid
            {
                Height = 38,
                Background = new SolidColorBrush(Color.FromRgb(21, 21, 21))
            };
            titleBar.ColumnDefinitions.Add(new ColumnDefinition());
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleBar.Children.Add(new TextBlock
            {
                Text = title,
                Margin = new Thickness(16, 0, 0, 0),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            var close = new Button
            {
                Content = "×",
                Width = 42,
                Height = 38,
                FontSize = 19,
                ToolTip = "Закрыть",
                Style = (Style)FindResource("CloseCaptionButton")
            };
            close.Click += (_, _) => Close();
            WindowChrome.SetIsHitTestVisibleInChrome(close, true);
            Grid.SetColumn(close, 1);
            titleBar.Children.Add(close);
            layout.Children.Add(titleBar);

            var scroll = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(scroll, 1);
            layout.Children.Add(scroll);
            Content = layout;
        }
    }
}
