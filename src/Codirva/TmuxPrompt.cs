namespace Codirva;

internal static class TmuxPrompt
{
    // Evaluate this in the tmux server immediately before the paste and Enter.
    private const string Ready = "#{&&:#{==:#{session_windows},1},"
        + "#{&&:#{==:#{window_panes},1},#{&&:#{==:#{pane_in_mode},0},"
        + "#{&&:#{||:#{==:#{pane_current_command},codex},#{==:#{pane_current_command},claude}},#{bracket_paste_flag}}}}}";

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
        var submit = $"paste-buffer -prd -b {buffer} -t {quotedTarget} ; "
            + $"send-keys -t {quotedTarget} Enter ; display-message -p sent";
        var reject = $"delete-buffer -b {buffer} ; display-message -p unavailable";

        var bufferRemoved = false;
        try
        {
            var (exitCode, output, error) = await TmuxDiscovery.RunAsync(session.Target,
                ["load-buffer", "-b", buffer, "-", ";", "if-shell", "-F", "-t", target, Ready, submit, reject],
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
                await TmuxDiscovery.RunAsync(session.Target, ["delete-buffer", "-b", buffer]).ConfigureAwait(false);
            }
        }
    }
}
