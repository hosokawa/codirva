using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RoyalTerminal.Terminal;

namespace Codirva;

public partial class MainWindow : Window
{
    private readonly Dictionary<TmuxSession, string> _drafts = [];
    private readonly SemaphoreSlim _terminalChange = new(1);
    private TmuxSession? _selectedSession;
    private TmuxSession? _attachedSession;
    private bool _refreshingSelection;
    private bool _sending;
    private bool _closed;

    public MainWindow()
    {
        InitializeComponent();
        PromptBox.AddHandler(KeyDownEvent, PromptKeyDown, RoutingStrategies.Tunnel);
        SessionTerminal.AddHandler(KeyDownEvent, TerminalCopyCompleted, RoutingStrategies.Bubble, handledEventsToo: true);
        SessionTerminal.ProcessExited += (_, _) =>
        {
            _attachedSession = null;
            ShowTerminalStatus("Disconnected. Select a session or use Refresh to reconnect.");
            UpdateComposer();
        };
        Opened += async (_, _) => await RefreshProjectsAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            if (_terminalChange.CurrentCount != 0 && SessionTerminal.HasActiveSession)
            {
                SessionTerminal.StopPty();
            }
        };
    }

    private async void RefreshClicked(object? sender, RoutedEventArgs e) =>
        await RefreshProjectsAsync();

    private async Task RefreshProjectsAsync()
    {
        RefreshButton.IsEnabled = false;
        ShowDiscoveryStatus("Loading sessions…");

        try
        {
            var targets = ConnectionConfig.Load();
            var results = await Task.WhenAll(targets.Distinct().Select(async target =>
            {
                try
                {
                    return (Projects: await TmuxDiscovery.ListProjectsAsync(target), Error: (string?)null);
                }
                catch (Exception error) when (error is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
                {
                    return (Projects: (IReadOnlyList<TmuxProject>)Array.Empty<TmuxProject>(),
                        Error: $"{target}: {error.Message}");
                }
            }));
            var projects = results.SelectMany(result => result.Projects)
                .GroupBy(project => project.Name, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new TmuxProject(group.Key, group.SelectMany(project => project.Sessions)
                    .OrderBy(session => session.Name, StringComparer.Ordinal)
                    .ThenBy(session => session.Target.DisplayName, StringComparer.Ordinal)
                    .ThenBy(session => session.Target.Distribution, StringComparer.Ordinal).ToArray()))
                .ToArray();
            var selected = ProjectsTree.SelectedItem as TmuxSession;
            _refreshingSelection = true;
            ProjectsTree.ItemsSource = projects;
            ProjectsTree.SelectedItem = projects.SelectMany(project => project.Sessions)
                .FirstOrDefault(session => session == selected);
            _refreshingSelection = false;
            var errors = results.Where(result => result.Error is not null).Select(result => result.Error);
            var message = string.Join("\n", errors);
            ShowDiscoveryStatus(message.Length > 0 ? $"Some connections could not be listed:\n{message}"
                : projects.Length == 0 ? "No matching tmux sessions." : null);
            await SelectSessionAsync();
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
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

    private async void ProjectSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingSelection)
        {
            await SelectSessionAsync();
        }
    }

    private async Task SelectSessionAsync()
    {
        var session = ProjectsTree.SelectedItem as TmuxSession;
        if (session != _selectedSession)
        {
            if (_selectedSession is not null)
            {
                _drafts[_selectedSession] = PromptBox.Text ?? "";
            }

            _selectedSession = session;
            PromptBox.Text = session is null ? "" : _drafts.GetValueOrDefault(session, "");
        }

        SessionHeading.Text = session is null
            ? "Select a session"
            : $"{session.ProjectName}({(session.Target.IsSsh ? "SSH" : "WSL")}: {session.Target.DisplayName}) - {session.FullName}";
        UpdateComposer();

        await _terminalChange.WaitAsync();
        try
        {
            if (_closed || session != _selectedSession || (session == _attachedSession && session is not null))
            {
                return;
            }

            _attachedSession = null;
            SessionTerminal.IsVisible = false;
            SessionTerminal.StopPty();
            ShowTerminalStatus(session is null ? null : "Connecting to tmux…");
            if (session is null)
            {
                return;
            }

            string[] arguments = session.Target.IsSsh
                ? ["-tt", "-o", "BatchMode=yes", "-o", "ConnectTimeout=10", "--",
                    session.Target.Distribution, $"TERM=xterm-256color tmux -u attach-session -t '{session.Id}'"]
                : [
                "--distribution", session.Target.Distribution, "--exec", "env", "TERM=xterm-256color",
                "tmux", "-u", "attach-session", "-t", session.Id,
            ];
            SessionTerminal.IsVisible = true;
            await SessionTerminal.StartSessionAsync(new PtyTransportOptions(
                new TerminalCommandSpec(session.Target.IsSsh ? "ssh.exe" : "wsl.exe", arguments),
                WorkingDirectory: null,
                Environment: null,
                new TerminalSessionDimensions(SessionTerminal.Columns, SessionTerminal.Rows,
                    (int)SessionTerminal.Bounds.Width, (int)SessionTerminal.Bounds.Height)));
            if (_closed || session != _selectedSession)
            {
                SessionTerminal.StopPty();
                return;
            }

            if (SessionTerminal.HasActiveSession)
            {
                _attachedSession = session;
                ShowTerminalStatus(null);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            ShowTerminalStatus($"Could not attach to tmux. {error.Message}");
        }
        finally
        {
            _terminalChange.Release();
            UpdateComposer();
        }
    }

    private void PromptChanged(object? sender, TextChangedEventArgs e) => UpdateComposer();

    private async void TerminalCopyCompleted(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Insert || e.KeyModifiers != KeyModifiers.Control || !SessionTerminal.HasSelection)
        {
            return;
        }

        try
        {
            await SessionTerminal.CopySelectionAsync();
            SessionTerminal.ClearSelection();
        }
        catch (Exception error)
        {
            ShowTerminalStatus($"Could not copy selection. {error.Message}");
        }
    }

    private async void SendClicked(object? sender, RoutedEventArgs e) => await SendPromptAsync();

    private async void PromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            await SendPromptAsync();
        }
    }

    private async Task SendPromptAsync()
    {
        var session = _attachedSession;
        var prompt = PromptBox.Text;
        if (_sending || session is null || session != _selectedSession || !SessionTerminal.HasActiveSession
            || string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        _sending = true;
        ProjectsTree.IsEnabled = false;
        UpdateComposer();
        try
        {
            await TmuxPrompt.SendAsync(session, prompt);
            if (_selectedSession == session)
            {
                PromptBox.Clear();
                ShowTerminalStatus(null);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            ShowTerminalStatus(error.Message);
        }
        finally
        {
            _sending = false;
            ProjectsTree.IsEnabled = true;
            UpdateComposer();
        }
    }

    private void UpdateComposer()
    {
        if (PromptBox is null || SendButton is null)
        {
            return;
        }

        PromptBox.IsEnabled = !_sending && _selectedSession is not null
            && _selectedSession == _attachedSession && SessionTerminal.HasActiveSession;
        SendButton.IsEnabled = PromptBox.IsEnabled && !string.IsNullOrWhiteSpace(PromptBox.Text);
    }

    private void ShowTerminalStatus(string? message)
    {
        TerminalStatus.Text = message;
        TerminalStatus.IsVisible = message is not null;
    }
}
