namespace Codirva.Tests;

public sealed class WindowsPasteModeTests
{
    [Fact]
    public void TracksPasteModeAcrossOutputChunksAndReplayTruncation()
    {
        using var host = WindowsSessions.CreateModeTracker();
        Assert.False(host.BracketedPaste);

        host.Process("\u001b[?20"u8);
        host.Process("04h"u8);
        Assert.True(host.BracketedPaste);

        // This much subsequent output pushes the enable sequence out of the 1 MiB replay window.
        var laterOutput = new byte[4096];
        Array.Fill(laterOutput, (byte)'x');
        for (var i = 0; i < 256; i++)
            host.Process(laterOutput);

        Assert.True(host.BracketedPaste);
        host.Process("\u001b[?2004l"u8);
        Assert.False(host.BracketedPaste);
    }
}
