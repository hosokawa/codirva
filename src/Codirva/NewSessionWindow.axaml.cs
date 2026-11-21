using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Codirva;

public partial class NewSessionWindow : Window
{
    public NewSessionWindow() => InitializeComponent();

    internal NewSessionWindow(IReadOnlyList<WslTarget> targets) : this()
    {
        ConnectionBox.ItemsSource = targets;
        ConnectionBox.SelectedIndex = 0;
    }

    private void AddClicked(object? sender, RoutedEventArgs e)
    {
        var group = GroupBox.Text?.Trim() ?? "";
        var name = NameBox.Text?.Trim() ?? "";
        if (ConnectionBox.SelectedItem is WslTarget target && SavedSession.IsValidName(group) && SavedSession.IsValidName(name))
        {
            Close(new SavedSession(target, $"{group}/{name}"));
            return;
        }

        ValidationMessage.Text = "Choose a connection and enter both names. Names cannot contain /, :, ., #, or control characters.";
        ValidationMessage.IsVisible = true;
    }

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close();
}
