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
        if (ConnectionBox.SelectedItem is WslTarget target && IsValidName(group) && IsValidName(name))
        {
            Close(new SavedSession(target, $"{group}/{name}"));
            return;
        }

        ValidationMessage.Text = "Choose a connection and enter both names. Names cannot contain /, :, ., #, or control characters.";
        ValidationMessage.IsVisible = true;
    }

    private static bool IsValidName(string name) =>
        name.Length > 0 && !name.Any(character => char.IsControl(character) || character is '/' or ':' or '.' or '#');

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close();
}
