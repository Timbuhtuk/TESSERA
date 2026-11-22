using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tessera.Desktop;

internal sealed class UnrealTextureDialog : Window
{
    internal ComboBox ProjectInput { get; } = new() { DisplayMemberPath = nameof(WorkspaceProject.Name), Margin = new Thickness(0, 6, 0, 14) };
    internal ComboBox FolderInput { get; } = new() { IsEditable = true, Margin = new Thickness(0, 6, 0, 14) };
    internal TextBox NameInput { get; } = new() { Margin = new Thickness(0, 6, 0, 14) };
    internal Button Confirm { get; } = new() { Content = Localization.Text("CreateTexture"), IsDefault = true };
    internal WorkspaceProject Project => (WorkspaceProject)ProjectInput.SelectedItem;
    internal string ObjectPath => FolderInput.Text.Trim().TrimEnd('/') + "/" + NameInput.Text.Trim() + "." + NameInput.Text.Trim();

    internal UnrealTextureDialog(IReadOnlyList<WorkspaceProject> projects, IReadOnlyList<WorkspaceFolder> folders,
        Guid? selectedProject, string initialFolder, string initialName, Func<WorkspaceProject, string, string?> validate,
        string? title = null, string? confirmText = null)
    {
        Title = title ?? Localization.Text("CreateTextureInUnreal");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (confirmText is not null) Confirm.Content = confirmText;
        Resources.MergedDictionaries.Add(new AppearanceResources());
        SetResourceReference(FontFamilyProperty, "UiFont");
        SetResourceReference(ForegroundProperty, "Ts.Text");
        var panel = new StackPanel();
        var heading = new TextBlock { Text = Title, FontSize = 20, Margin = new Thickness(0, 0, 0, 18) };
        heading.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        panel.Children.Add(heading);
        panel.Children.Add(new TextBlock { Text = Localization.Text("UnrealEngineProject") });
        panel.Children.Add(ProjectInput);
        panel.Children.Add(new TextBlock { Text = Localization.Text("UnrealFolderGame") });
        panel.Children.Add(FolderInput);
        panel.Children.Add(new TextBlock { Text = Localization.Text("TextureName") });
        panel.Children.Add(NameInput);
        AutomationProperties.SetName(ProjectInput, Localization.Text("UnrealEngineProject"));
        AutomationProperties.SetName(FolderInput, Localization.Text("UnrealFolderGame"));
        AutomationProperties.SetName(NameInput, Localization.Text("TextureName"));
        var validation = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        validation.SetResourceReference(ForegroundProperty, "Ts.TextSecondary");
        panel.Children.Add(validation);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = Localization.Text("Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Style = (Style)FindResource("Ts.CompactButton");
        Confirm.Style = (Style)FindResource("Ts.PrimaryButton");
        buttons.Children.Add(cancel);
        buttons.Children.Add(Confirm);
        panel.Children.Add(buttons);
        Content = new Border { Style = (Style)FindResource("Ts.DialogBorder"), Margin = new Thickness(14), Child = panel };

        void Validate()
        {
            validation.Text = ProjectInput.SelectedItem is WorkspaceProject project ? validate(project, ObjectPath) : Localization.Text("ReconnectTheUnrealProject");
            Confirm.IsEnabled = string.IsNullOrEmpty(validation.Text);
        }
        void SelectProject()
        {
            FolderInput.ItemsSource = folders.Where(f => f.ProjectId == Project.Id && f.UnrealPath is not null)
                .Select(f => f.UnrealPath!).Append("/Game").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToArray();
        }
        ProjectInput.ItemsSource = projects;
        ProjectInput.SelectedItem = projects.FirstOrDefault(p => p.Id == selectedProject) ?? projects[0];
        SelectProject();
        FolderInput.Text = initialFolder;
        NameInput.Text = initialName;
        ProjectInput.SelectionChanged += (_, _) => { SelectProject(); FolderInput.Text = "/Game"; Validate(); };
        FolderInput.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => Validate()));
        NameInput.TextChanged += (_, _) => Validate();
        Confirm.Click += (_, _) => { Validate(); if (Confirm.IsEnabled) DialogResult = true; };
        Loaded += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); };
        Validate();
    }
}
