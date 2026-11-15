using System.Diagnostics;
using System.Text;

namespace Codirva;

internal sealed record TmuxProject(string Name, IReadOnlyList<TmuxSession> Sessions);

internal sealed record WslTarget(string Distribution, string DisplayName);

internal sealed record TmuxSession(string Name, string FullName, string ProjectName, WslTarget Target);

internal static class TmuxDiscovery
{
    public static async Task<IReadOnlyList<TmuxProject>> ListProjectsAsync(WslTarget target)
    {
        var startInfo = new ProcessStartInfo("wsl.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in new[]
        {
            "--distribution", target.Distribution, "--exec", "env", "LC_ALL=C",
            "tmux", "list-sessions", "-F", "#{session_name}",
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start wsl.exe.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = (await errorTask.ConfigureAwait(false)).Replace("\0", "").Trim();

        if (process.ExitCode != 0)
        {
            if (error.StartsWith("no server running on ", StringComparison.Ordinal)
                || (error.StartsWith("error connecting to ", StringComparison.Ordinal)
                    && error.EndsWith("(No such file or directory)", StringComparison.Ordinal)))
            {
                return [];
            }

            throw new InvalidOperationException(error.Length == 0
                ? $"wsl.exe exited with code {process.ExitCode}."
                : error);
        }

        var projects = new SortedDictionary<string, List<TmuxSession>>(StringComparer.Ordinal);
        foreach (var name in output.Split('\n'))
        {
            var fullName = name.TrimEnd('\r');
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

            sessions.Add(new TmuxSession(fullName[(separator + 1)..], fullName, projectName, target));
        }

        return projects.Select(project => new TmuxProject(project.Key, project.Value
            .OrderBy(session => session.Name, StringComparer.Ordinal)
            .ToArray())).ToArray();
    }
}
