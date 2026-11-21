using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;

namespace Codirva;

public partial class MainWindow : Window
{
    private readonly Dictionary<(string Kind, string Address, string Name), string> _drafts = [];
    private readonly SemaphoreSlim _terminalChange = new(1);
    private TmuxSession? _selectedSession;
    private TmuxSession? _attachedSession;
    private Guid _windowsClient;
    private (int Columns, int Rows)? _pendingWindowsSize;
    private bool _resizingWindowsSession;
    private bool _refreshingSelection;
    private bool _managingSessions;
    private bool _sending;
    private bool _closed;

    public MainWindow()
    {
        InitializeComponent();
        MainWindowPlacement.Remember(this);
        PromptBox.AddHandler(KeyDownEvent, PromptKeyDown, RoutingStrategies.Tunnel);
        SessionTerminal.AddHandler(KeyDownEvent, TerminalCopyCompleted, RoutingStrategies.Bubble, handledEventsToo: true);
        SessionTerminal.TerminalResized += WindowsTerminalResized;
        SessionTerminal.ProcessExited += (_, _) =>
        {
            _attachedSession = null;
            ShowTerminalStatus("Disconnected. Select a session or use Refresh to reconnect.");
            UpdateComposer();
        };
        Opened += async (_, _) => await ManageSessionsAsync(() => RefreshProjectsAsync());
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
        await ManageSessionsAsync(() => RefreshProjectsAsync());

    private void ExitClicked(object? sender, RoutedEventArgs e) => Close();

    private async void NewSessionClicked(object? sender, RoutedEventArgs e) =>
        await ManageSessionsAsync(async () =>
        {
            var dialog = new NewSessionWindow(ConnectionConfig.Load());
            var session = await dialog.ShowDialog<SavedSession?>(this);
            if (session is null)
            {
                return;
            }

            var saved = SavedSessions.Load();
            await StartSessionAsync(session);
            SavedSessions.Save(saved.Where(item => item.Key != session.Key).Append(session));
            await RefreshProjectsAsync(session);
        });

    private async void StartClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TmuxSession { CanStart: true } session })
        {
            await ManageSessionsAsync(async () =>
            {
                await StartSessionAsync(session.Saved);
                await RefreshProjectsAsync(session.Saved);
            });
        }
    }

    private async void ForgetClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { CommandParameter: TmuxSession { CanStart: true } session })
        {
            await ManageSessionsAsync(async () =>
            {
                var projects = await ListProjectsAsync(session.Target, SavedSessions.Load());
                if (!projects.SelectMany(project => project.Sessions).Any(item => item.FullName == session.FullName))
                {
                    SavedSessions.Save(SavedSessions.Load().Where(item => item.Key != session.Saved.Key));
                }

                await RefreshProjectsAsync();
            });
        }
    }

    private async Task ManageSessionsAsync(Func<Task> action)
    {
        if (_managingSessions || _sending || _closed)
        {
            return;
        }

        _managingSessions = true;
        NewSessionButton.IsEnabled = false;
        UpdateComposer();
        try
        {
            await action();
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException
            or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        {
            ShowDiscoveryStatus(error.Message);
        }
        finally
        {
            _managingSessions = false;
            NewSessionButton.IsEnabled = true;
            UpdateComposer();
        }
    }

    private async Task RefreshProjectsAsync(SavedSession? select = null)
    {
        ShowDiscoveryStatus("Loading sessions…");
        var saved = SavedSessions.Load();
        var targets = ConnectionConfig.Load().DistinctBy(target => (target.Kind, target.Distribution));
        var results = await Task.WhenAll(targets.Select(async target =>
        {
            try
            {
                return (Target: target, Projects: await ListProjectsAsync(target, saved), Error: (string?)null);
            }
            catch (Exception error) when (error is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
            {
                return (Target: target, Projects: (IReadOnlyList<TmuxProject>)Array.Empty<TmuxProject>(),
                    Error: $"{target}: {error.Message}");
            }
        }));
        if (_closed)
        {
            return;
        }

        var sessions = results.SelectMany(result => result.Projects)
            .SelectMany(project => project.Sessions).ToList();
        var runningKeys = sessions.Select(session => session.Saved.Key).ToHashSet();
        foreach (var entry in saved.Where(entry => !runningKeys.Contains(entry.Key)))
        {
            var result = results.FirstOrDefault(result => result.Target.Kind == entry.Target.Kind
                && result.Target.Distribution == entry.Target.Distribution);
            var names = entry.FullName.Split('/', 2);
            sessions.Add(new TmuxSession(names[1], entry.FullName, names[0], result.Target ?? entry.Target, "")
            {
                CanStart = result.Target is not null && result.Error is null,
            });
        }

        var projects = sessions.GroupBy(session => session.ProjectName, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new TmuxProject(group.Key, group.OrderBy(session => session.Name, StringComparer.Ordinal)
                .ThenBy(session => session.Target.DisplayName, StringComparer.Ordinal)
                .ThenBy(session => session.Target.Distribution, StringComparer.Ordinal).ToArray()))
            .ToArray();
        var selected = select ?? (ProjectsTree.SelectedItem as TmuxSession)?.Saved;
        _refreshingSelection = true;
        ProjectsTree.ItemsSource = projects;
        ProjectsTree.SelectedItem = sessions.FirstOrDefault(session => session.Saved.Key == selected?.Key);
        _refreshingSelection = false;
        var errors = results.Where(result => result.Error is not null).Select(result => result.Error);
        var message = string.Join("\n", errors);
        ShowDiscoveryStatus(message.Length > 0 ? $"Some connections could not be listed:\n{message}"
            : projects.Length == 0 ? "No matching tmux sessions." : null);
        await SelectSessionAsync();
    }

    private static Task StartSessionAsync(SavedSession session) => session.Target.IsWindows
        ? WindowsSessions.StartAsync(session) : TmuxDiscovery.CreateSessionAsync(session);

    private static Task<IReadOnlyList<TmuxProject>> ListProjectsAsync(WslTarget target, IReadOnlyList<SavedSession> saved) =>
        target.IsWindows ? Task.FromResult(WindowsSessions.ListProjects(saved)) : TmuxDiscovery.ListProjectsAsync(target);

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
                _drafts[_selectedSession.Saved.Key] = PromptBox.Text ?? "";
            }

            _selectedSession = session;
            PromptBox.Text = session is null ? "" : _drafts.GetValueOrDefault(session.Saved.Key, "");
        }

        SessionHeading.Text = session is null
            ? "Select a session"
            : $"{session.ProjectName}({session.Target.Kind}: {session.Target.DisplayName}) - {session.FullName}";
        UpdateComposer();

        await _terminalChange.WaitAsync();
        try
        {
            if (_closed || session != _selectedSession || (session == _attachedSession && session is not null))
            {
                return;
            }

            _attachedSession = null;
            _pendingWindowsSize = null;
            SessionTerminal.IsVisible = false;
            SessionTerminal.StopPty();
            ShowTerminalStatus(session switch
            {
                null => null,
                { IsRunning: true } => "Connecting to session…",
                { CanStart: true } => "Session is stopped.",
                _ => "Session status is unavailable.",
            });
            if (session is not { IsRunning: true })
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
            // ConPTY owns the Windows screen's resize and sends its updates back over the relay.
            SessionTerminal.ReflowOnResize = !session.Target.IsWindows;
            SessionTerminal.IsVisible = true;
            var dimensions = new TerminalSessionDimensions(SessionTerminal.Columns, SessionTerminal.Rows,
                (int)SessionTerminal.Bounds.Width, (int)SessionTerminal.Bounds.Height);
            _windowsClient = Guid.NewGuid();
            ITerminalTransportOptions options = session.Target.IsWindows
                ? new PipeTransportOptions(WindowsSessions.RelayCommand(session.Id, _windowsClient, dimensions), null, null, false, dimensions)
                : new PtyTransportOptions(new TerminalCommandSpec(session.Target.IsSsh ? "ssh.exe" : "wsl.exe", arguments),
                    null, null, dimensions);
            await SessionTerminal.StartSessionAsync(options);
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
            ShowTerminalStatus($"Could not attach to the session. {error.Message}");
        }
        finally
        {
            _terminalChange.Release();
            UpdateComposer();
        }
    }

    private async void WindowsTerminalResized(object? sender, TerminalSizeEventArgs size)
    {
        if (_attachedSession is not { Target.IsWindows: true })
            return;

        _pendingWindowsSize = (size.Columns, size.Rows);
        if (_resizingWindowsSession)
            return;

        _resizingWindowsSession = true;
        try
        {
            while (_pendingWindowsSize is not null)
            {
                // Coalesce a drag's intermediate sizes and keep host requests in order.
                await Task.Delay(75);
                var latest = _pendingWindowsSize;
                _pendingWindowsSize = null;
                if (latest is not { } dimensions || _attachedSession is not { Target.IsWindows: true } session)
                    continue;
                try
                {
                    await WindowsSessions.ResizeAsync(session.Id, _windowsClient, dimensions.Columns, dimensions.Rows);
                }
                catch (Exception error) when (error is IOException or InvalidOperationException)
                {
                    // A resize can race with disconnecting the relay.
                }
            }
        }
        finally
        {
            _resizingWindowsSession = false;
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
            if (session.Target.IsWindows)
            {
                if (SessionTerminal.TerminalSessionService.ModeSource?.ModeState.BracketedPaste != true)
                    throw new InvalidOperationException("Start a supported agent with bracketed paste enabled in the Windows terminal before sending a prompt.");
                await WindowsSessions.SubmitAsync(session.Id, _windowsClient, prompt, session.Target.IsWindowsCodex);
            }
            else
            {
                await TmuxPrompt.SendAsync(session, prompt);
            }
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

        PromptBox.IsEnabled = !_sending && !_managingSessions && _selectedSession is not null
            && _selectedSession == _attachedSession && SessionTerminal.HasActiveSession;
        SendButton.IsEnabled = PromptBox.IsEnabled && !string.IsNullOrWhiteSpace(PromptBox.Text);
    }

    private void ShowTerminalStatus(string? message)
    {
        TerminalStatus.Text = message;
        TerminalStatus.IsVisible = message is not null;
    }
}
