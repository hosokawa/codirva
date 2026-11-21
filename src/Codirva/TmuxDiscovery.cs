using System.Diagnostics;
using System.Text;

namespace Codirva;

internal sealed record TmuxProject(string Name, IReadOnlyList<TmuxSession> Sessions);

internal sealed record WslTarget(string Distribution, string DisplayName, bool IsSsh = false, bool IsWindows = false,
    bool IsWindowsCodex = false)
{
    public string Kind => IsWindowsCodex ? "ConPTY Codex" : IsWindows ? "ConPTY" : IsSsh ? "SSH" : "WSL";
    public override string ToString() => $"{DisplayName} ({Kind})";
}

internal sealed record TmuxSession(string Name, string FullName, string ProjectName, WslTarget Target, string Id)
{
    public bool CanStart { get; init; }
    public bool IsRunning => Id.Length != 0;
    public SavedSession Saved => new(Target, FullName);
    public string DisplayName => Target.IsWindows ? $"{Name} (Windows: {Target.Kind})"
        : $"{Name} ({Target.DisplayName}: {Target.Kind} {Target.Distribution})";
}

internal static class TmuxDiscovery
{
    public static async Task CreateSessionAsync(SavedSession session)
    {
        var (exitCode, _, error) = await RunAsync(session.Target,
            ["new-session", "-d", "-s", session.FullName, "-c", "#{HOME}",
                "sh", "-c", "exec \"$SHELL\" -l"]).ConfigureAwait(false);
        if (exitCode != 0)
        {
            // Another client may have created the same session first.
            var projects = await ListProjectsAsync(session.Target).ConfigureAwait(false);
            if (!projects.SelectMany(project => project.Sessions).Any(item => item.FullName == session.FullName))
            {
                throw new InvalidOperationException(error.Length == 0
                    ? $"tmux exited with code {exitCode}."
                    : error);
            }
        }
    }

    public static async Task<IReadOnlyList<TmuxProject>> ListProjectsAsync(WslTarget target)
    {
        var (exitCode, output, error) = await RunAsync(target,
            ["list-sessions", "-F", "#{session_id}|#{session_name}"]).ConfigureAwait(false);

        if (exitCode != 0)
        {
            if (error.StartsWith("no server running on ", StringComparison.Ordinal)
                || (error.StartsWith("error connecting to ", StringComparison.Ordinal)
                    && error.EndsWith("(No such file or directory)", StringComparison.Ordinal)))
            {
                return [];
            }

            throw new InvalidOperationException(error.Length == 0
                ? $"Connection command exited with code {exitCode}."
                : error);
        }

        var projects = new SortedDictionary<string, List<TmuxSession>>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.TrimEnd('\r').Split('|', 2);
            if (fields.Length != 2 || fields[0].Length < 2 || fields[0][0] != '$'
                || fields[0].AsSpan(1).IndexOfAnyExceptInRange('0', '9') >= 0)
            {
                continue;
            }

            var fullName = fields[1];
            var separator = fullName.IndexOf('/');
            if (separator <= 0 || separator == fullName.Length - 1
                || fullName.IndexOf('/', separator + 1) >= 0)
            {
                continue;
            }

            var projectName = fullName[..separator];
            if (!projects.TryGetValue(projectName, out var sessions))
            {
                sessions = [];
                projects.Add(projectName, sessions);
            }

            sessions.Add(new TmuxSession(fullName[(separator + 1)..], fullName, projectName, target, fields[0]));
        }

        return projects.Select(project => new TmuxProject(project.Key, project.Value
            .OrderBy(session => session.Name, StringComparer.Ordinal)
            .ToArray())).ToArray();
    }

    internal static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        WslTarget target, string[] arguments, string? input = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        var startInfo = new ProcessStartInfo(target.IsSsh ? "ssh.exe" : "wsl.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = input is null ? null : new UTF8Encoding(false),
        };

        foreach (var argument in target.IsSsh
            ? new[] { "-o", "BatchMode=yes", "-o", "ConnectTimeout=10", "--", target.Distribution }
            : new[] { "--distribution", target.Distribution, "--exec", "env", "LC_ALL=C", "tmux" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (target.IsSsh)
        {
            // OpenSSH passes a single command through the remote shell.
            static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
            startInfo.ArgumentList.Add("LC_ALL=C tmux " + string.Join(" ", arguments.Select(Quote)));
        }
        else foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {startInfo.FileName}.");
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        try
        {
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), token).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(token).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            await Task.WhenAll(process.WaitForExitAsync(token), output, error).WaitAsync(token).ConfigureAwait(false);
            return (process.ExitCode, await output.ConfigureAwait(false),
                (await error.ConfigureAwait(false)).Replace("\0", "").Trim());
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (Exception cleanupError) when (cleanupError is InvalidOperationException
                or System.ComponentModel.Win32Exception or TimeoutException)
            {
                Trace.TraceWarning("Could not stop {0}: {1}", startInfo.FileName, cleanupError.Message);
            }
            if (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                throw new IOException($"tmux did not respond within 20 seconds on {target}.");
            throw;
        }
    }
}
