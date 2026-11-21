using Avalonia;

namespace Codirva;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is [var command, var id] && WindowsSessions.IsSessionId(id))
        {
            if (command == "--windows-session-host")
                return WindowsSessions.HostAsync(id).GetAwaiter().GetResult();
        }
        if (args is ["--windows-session-relay", var relayId, var clientId, var width, var height]
            && WindowsSessions.IsSessionId(relayId) && Guid.TryParseExact(clientId, "N", out var client)
            && int.TryParse(width, out var columns) && int.TryParse(height, out var rows))
            return WindowsSessions.RelayAsync(relayId, client, columns, rows).GetAwaiter().GetResult();

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
    }
}
