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
        gridToolButton.Click += (_, _) => OpenEditorTool("grid", "Сетка", 300, gridToolGroup);
        sizeToolButton.Click += (_, _) => OpenEditorTool("size", "Размер", 660, sizeToolGroup, frameToolGroup, pixelToolGroup);
        colorToolButton.Click += (_, _) => OpenEditorTool("color", "Цвет", 500, colorToolGroup);
        profileToolButton.Click += (_, _) => OpenEditorTool("profile", "Режим", 440, profileToolGroup, executionToolGroup);
        fileMenuButton.Click += (_, _) => fileMenuPopup.IsOpen = !fileMenuPopup.IsOpen;
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
        foreach (var group in groups)
        {
            if (group.Parent is not Panel parent) throw new InvalidOperationException("Панель инструмента уже используется.");
            parent.Children.Remove(group);
            content.Children.Add(group);
        }

        var tool = new EditorToolWindow(this, title, content, height);
        tool.Closed += (_, _) =>
        {
            _toolWindows.Remove(key);
            foreach (var group in groups)
            {
                content.Children.Remove(group);
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
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/DomainColorTest;component/Theme.xaml", UriKind.Relative)
            });
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
