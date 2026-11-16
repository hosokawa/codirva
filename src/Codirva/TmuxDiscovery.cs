using System.Diagnostics;
using System.Text;

namespace Codirva;

internal sealed record TmuxProject(string Name, IReadOnlyList<TmuxSession> Sessions);

internal sealed record WslTarget(string Distribution, string DisplayName, bool IsSsh = false)
{
    public override string ToString() => $"{DisplayName} ({(IsSsh ? "SSH" : "WSL")})";
}

internal sealed record TmuxSession(string Name, string FullName, string ProjectName, WslTarget Target, string Id)
{
    public string DisplayName => $"{Name} ({Target.DisplayName}: {(Target.IsSsh ? "SSH" : "WSL")} {Target.Distribution})";
}

internal static class TmuxDiscovery
{
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
        WslTarget target, string[] arguments, string? input = null)
    {
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
            ?? throw new InvalidOperationException("Could not start wsl.exe.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (input is not null)
        {
            await process.StandardInput.WriteAsync(input).ConfigureAwait(false);
            await process.StandardInput.DisposeAsync().ConfigureAwait(false);
        }

        await process.WaitForExitAsync().ConfigureAwait(false);
        return (process.ExitCode, await output.ConfigureAwait(false),
            (await error.ConfigureAwait(false)).Replace("\0", "").Trim());
    }
}
