using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Codirva;

public partial class MainWindow : Window
{
    private static readonly WslTarget Target = new("archlinux", "Arch Linux");

    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => await RefreshProjectsAsync();
    }

    private async void RefreshClicked(object? sender, RoutedEventArgs e) =>
        await RefreshProjectsAsync();

    private async Task RefreshProjectsAsync()
    {
        RefreshButton.IsEnabled = false;
        ShowDiscoveryStatus("Loading sessions…");

        try
        {
            var projects = await TmuxDiscovery.ListProjectsAsync(Target);
            var selected = ProjectsTree.SelectedItem as TmuxSession;
            ProjectsTree.ItemsSource = projects;
            ProjectsTree.SelectedItem = projects.SelectMany(project => project.Sessions)
                .FirstOrDefault(session => session == selected);
            ShowDiscoveryStatus(projects.Count == 0 ? "No matching tmux sessions." : null);
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ProjectsTree.ItemsSource = null;
            ShowDiscoveryStatus($"Could not list tmux sessions. {error.Message}");
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void ShowDiscoveryStatus(string? message)
    {
        DiscoveryStatus.Text = message;
        DiscoveryStatus.IsVisible = message is not null;
    }

    private void ProjectSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var session = ProjectsTree.SelectedItem as TmuxSession;
        SessionHeading.Text = session is null
            ? "Select a session"
            : $"{session.ProjectName}(WSL: {session.Target.DisplayName}) - {session.FullName}";
    }
}
