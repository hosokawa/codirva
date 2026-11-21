using System.Text.Json;

namespace Codirva;

internal static class ConnectionConfig
{
    private sealed record Config(List<TargetConfig>? Targets);
    private sealed record TargetConfig(string? Name, string? Type, string? Distribution, string? Host);

    public static string PathName
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var directory = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Codirva")
                : OperatingSystem.IsMacOS()
                    ? Path.Combine(home, "Library", "Application Support", "Codirva")
                    : Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg)
                        ? xdg : Path.Combine(home, ".config"), "codirva");
            return Path.Combine(directory, "config.json");
        }
    }

    public static IReadOnlyList<WslTarget> Load()
    {
        var targets = new List<WslTarget>();
        if (OperatingSystem.IsWindows())
        {
            targets.Add(WindowsSessions.Target);
        }
        if (!File.Exists(PathName) && targets.Count > 0)
        {
            return targets;
        }

        try
        {
            var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(PathName),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (config?.Targets is not { Count: > 0 })
                throw new InvalidOperationException("The targets array must contain at least one connection.");
            foreach (var entry in config.Targets)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.Name))
                    throw new InvalidOperationException("Each target must have a name.");
                var isSsh = entry.Type == "ssh";
                if (!isSsh && entry.Type != "wsl")
                    throw new InvalidOperationException($"Target '{entry.Name}' must have type 'ssh' or 'wsl'.");
                var address = (isSsh ? entry.Host : entry.Distribution)?.Trim();
                if (string.IsNullOrEmpty(address) || address.StartsWith('-') || address.Any(char.IsWhiteSpace))
                    throw new InvalidOperationException($"Target '{entry.Name}' has an invalid host or distribution.");
                targets.Add(new WslTarget(address, entry.Name, isSsh));
            }
            return targets;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException($"Could not read configuration at {PathName}: {error.Message}", error);
        }
    }
}
