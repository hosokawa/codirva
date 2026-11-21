namespace Codirva.Tests;

public sealed class WindowsProcessesTests
{
    [Theory]
    [InlineData("codex.exe")]
    [InlineData("CLAUDE.EXE")]
    [InlineData("pi.exe")]
    [InlineData("dsh.exe")]
    public void FindsOneSupportedDescendant(string executable)
    {
        Assert.Equal(12u, WindowsProcesses.FindSingleAgent(10,
            [(11u, 10u, "wrapper.exe"), (12u, 11u, executable), (13u, 99u, "codex.exe")]));
    }

    [Fact]
    public void RejectsTwoSupportedDescendants()
    {
        Assert.Null(WindowsProcesses.FindSingleAgent(10,
            [(11u, 10u, "codex.exe"), (12u, 10u, "pi.exe")]));
    }

    [Fact]
    public void RejectsUnsupportedNamesAndUnrelatedAgents()
    {
        Assert.Null(WindowsProcesses.FindSingleAgent(10,
            [(11u, 10u, "codex-helper.exe"), (12u, 99u, "codex.exe")]));
    }
}
