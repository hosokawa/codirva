namespace Codirva;

internal static class TmuxPrompt
{
    private const string PaneReady = "#{&&:#{==:#{session_windows},1},"
        + "#{&&:#{==:#{window_panes},1},#{&&:#{==:#{pane_in_mode},0},"
        + "#{bracket_paste_flag}}}}";
    private const string NativeAgent = "#{||:#{==:#{pane_current_command},codex},#{==:#{pane_current_command},claude}}";
    private const string NativeReady = "#{&&:" + PaneReady + "," + NativeAgent + "}";

    // The marker alone is insufficient if the launcher was killed before its exit trap ran.
    private const string WslReady = $$"""
        [ "{{PaneReady}}" = 1 ] || exit 1
        [ "{{NativeAgent}}" != 1 ] || exit 0
        pid=#{q:@codirva_windows_codex_pid}
        case "$pid" in ''|*[!0-9]*) exit 1;; esac
        case "$(ps -o stat= -p "$pid")" in *+*) ;; *) exit 1;; esac
        ps -o comm= --ppid "$pid" | grep -qx 'powershell\.exe'
        """;

    public static async Task SendAsync(TmuxSession session, string prompt)
    {
        if (prompt.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
        {
            throw new InvalidOperationException("The prompt contains unsupported control characters.");
        }

        var target = session.Id + ":";
        var buffer = $"codirva-{Guid.NewGuid():N}";
        // The target ID comes from tmux and contains only '$' and digits.
        var quotedTarget = $"'{target}'";
        // Windows interop can deliver Enter before Codex finishes handling the bracketed paste.
        var waitForWindowsPaste = session.Target.IsSsh ? ""
            : $"run-shell -t {quotedTarget} 'if [ \"{NativeAgent}\" != 1 ]; then sleep 0.25; fi' ; ";
        var submit = $"paste-buffer -prd -b {buffer} -t {quotedTarget} ; " + waitForWindowsPaste
            + $"send-keys -t {quotedTarget} Enter ; display-message -p sent";
        var reject = $"delete-buffer -b {buffer} ; display-message -p unavailable";

        var bufferRemoved = false;
        try
        {
            string[] condition = session.Target.IsSsh ? ["-F", "-t", target, NativeReady] : ["-t", target, WslReady];
            var (exitCode, output, error) = await TmuxDiscovery.RunAsync(session.Target,
                ["load-buffer", "-b", buffer, "-", ";", "if-shell", .. condition, submit, reject],
                prompt.ReplaceLineEndings("\n")).ConfigureAwait(false);

            if (exitCode != 0)
            {
                throw new InvalidOperationException(error.Length == 0 ? "tmux could not send the prompt." : error);
            }

            var result = output.Trim();
            bufferRemoved = result is "sent" or "unavailable";
            if (result != "sent")
            {
                throw new InvalidOperationException("Sending requires one Codex CLI or Claude Code pane with bracketed paste enabled and copy mode off.");
            }
        }
        finally
        {
            // A vanished session can prevent the queued paste or rejection from deleting the buffer.
            if (!bufferRemoved)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    await TmuxDiscovery.RunAsync(session.Target, ["delete-buffer", "-b", buffer],
                        cancellationToken: cleanup.Token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or InvalidOperationException
                    or OperationCanceledException or System.ComponentModel.Win32Exception)
                {
                    System.Diagnostics.Trace.TraceWarning("Could not clean up tmux paste buffer: {0}", error.Message);
                }
            }
        }
    }
}
