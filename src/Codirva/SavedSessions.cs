using System.Text.Json;

namespace Codirva;

internal sealed record SavedSession(WslTarget Target, string FullName)
{
    public (string Kind, string Address, string Name) Key => (Target.Kind, Target.Distribution, FullName);

    public static bool IsValidName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && !name.Any(character => char.IsControl(character) || character is '/' or ':' or '.' or '#');
}

internal static class SavedSessions
{
    private static string FilePath => Path.Combine(Path.GetDirectoryName(ConnectionConfig.PathName)!, "sessions.json");

    public static IReadOnlyList<SavedSession> Load() => Load(FilePath);

    internal static IReadOnlyList<SavedSession> Load(string path)
    {
        try
        {
            var sessions = JsonSerializer.Deserialize<SavedSession[]>(File.ReadAllText(path))
                ?? throw new InvalidOperationException("Invalid saved session list.");
            for (var i = 0; i < sessions.Length; i++)
            {
                var session = sessions[i];
                if (session?.Target is not { } target || string.IsNullOrWhiteSpace(target.DisplayName)
                    || string.IsNullOrWhiteSpace(target.Distribution) || target.Distribution.StartsWith('-')
                    || target.Distribution.Any(char.IsWhiteSpace) || (target.IsSsh && target.IsWindows)
                    || (target.IsWindowsCodex && !target.IsWindows)
                    || session.FullName?.Split('/') is not [var project, var name]
                    || !SavedSession.IsValidName(project) || !SavedSession.IsValidName(name))
                    throw new InvalidOperationException($"Invalid saved session #{i + 1} in {path}.");
            }
            return OperatingSystem.IsWindows()
                ? sessions.Select(WindowsSessions.NormalizeSavedSession).DistinctBy(session => session.Key).ToArray()
                : sessions;
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

    public static void Save(IEnumerable<SavedSession> sessions)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporaryPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(sessions));
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
