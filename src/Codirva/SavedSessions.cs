using System.Text.Json;

namespace Codirva;

internal sealed record SavedSession(WslTarget Target, string FullName)
{
    public (bool IsSsh, string Address, string Name) Key => (Target.IsSsh, Target.Distribution, FullName);
}

internal static class SavedSessions
{
    private static string FilePath => Path.Combine(Path.GetDirectoryName(ConnectionConfig.PathName)!, "sessions.json");

    public static IReadOnlyList<SavedSession> Load()
    {
        try
        {
            return JsonSerializer.Deserialize<SavedSession[]>(File.ReadAllText(FilePath))
                ?? throw new InvalidOperationException("Invalid saved session list.");
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
