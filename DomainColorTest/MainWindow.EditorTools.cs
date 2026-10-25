using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Shell;

namespace DomainColorTest;

public partial class MainWindow
{
    private readonly Dictionary<string, EditorToolWindow> _toolWindows = [];

    private void OpenEditorTool(string key, string title, double height, params GroupBox[] groups)
    {
        if (_workspaceReady) { OpenDockedWorkspaceTool(key switch { "profile" => Localization.Text("Готовые сценарии"), "color" => Localization.Text("Уменьшить число цветов"), "smoothing" => Localization.Text("Очистить цвета"), "info" => Localization.Text("Цвета и сведения"), _ => title }, groups); _cropToolActive = key == "size"; UpdateCropPreview(); SetActiveLivePreviewTool(key); if (key == "info") RefreshInfo(); if (key is "color" or "smoothing") AutoEnableLivePreview(key); return; }
        _cropToolActive = key == "size";
        UpdateCropPreview();
        SetActiveLivePreviewTool(key);
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
            if (group.Parent is not Panel parent) throw new InvalidOperationException(Localization.Text("Панель инструмента уже используется."));
            // Сохраняем стили при переносе: иначе Slider временно получает Maximum = 10 и меняет Value.
            if (!group.Resources.MergedDictionaries.Contains(Resources)) group.Resources.MergedDictionaries.Add(Resources);
            parent.Children.Remove(group);
            if (index == 0) content.Children.Add(group);
            else advancedContent!.Children.Add(group);
        }
        if (advancedContent is not null)
            content.Children.Add(new Expander
            {
                Header = Localization.Text("Дополнительные настройки"),
                Content = advancedContent,
                Style = (Style)FindResource("ToolExpander"),
                Margin = new Thickness(0, 2, 0, 8)
            });

        var tool = new EditorToolWindow(this, title, content, height);
        tool.Activated += (_, _) => { _cropToolActive = key == "size"; UpdateCropPreview(); SetActiveLivePreviewTool(key); };
        tool.Closed += (_, _) =>
        {
            _toolWindows.Remove(key);
            if (_cropToolActive && key == "size") { _cropToolActive = false; UpdateCropPreview(); }
            if (ActiveLivePreviewTool == key) SetActiveLivePreviewTool(null);
            if (key == "info") ResetInfoSnapshot();
            foreach (var group in groups)
            {
                if (group.Parent is Panel parent) parent.Children.Remove(group);
                settingsPanel.Children.Add(group);
            }
        };
        _toolWindows.Add(key, tool);
        tool.Show();
        if (key is "color" or "smoothing") AutoEnableLivePreview(key);
    }

    private void CloseEditorTools()
    {
        CloseDockedWorkspaceTool();
        foreach (var tool in _toolWindows.Values.ToArray()) tool.Close();
        CloseNavigationMenus();
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
            SetResourceReference(BackgroundProperty, "Ts.Canvas");
            SetResourceReference(ForegroundProperty, "Ts.Text");
            FontFamily = (FontFamily)owner.FindResource("UiFont");
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
                else if (e.Key == Key.S && owner.saveMenuItem.IsEnabled) owner.SaveImage(this, EventArgs.Empty);
                else return;
                e.Handled = true;
            };

            ToolContent = content;
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition());

            var titleBar = new Grid
            {
                Height = 38
            };
            titleBar.SetResourceReference(Panel.BackgroundProperty, "Ts.Raised");
            titleBar.ColumnDefinitions.Add(new ColumnDefinition());
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleBar.Children.Add(new TextBlock
            {
                Text = title,
                FontFamily = (FontFamily)owner.FindResource("DisplayFont"),
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
                ToolTip = Localization.Text("Закрыть"),
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
