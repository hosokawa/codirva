namespace Codirva.Tests;

public sealed class SavedSessionsTests
{
    [Theory]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("""[{"Target":{"Distribution":"local","DisplayName":"Windows","IsWindows":true},"FullName":"invalid"}]""")]
    public void LoadRejectsMalformedEntriesAndPreservesFile(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            var error = Assert.Throws<InvalidOperationException>(() => SavedSessions.Load(path));
            Assert.Contains("#1", error.Message);
            Assert.Equal(json, File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DraftIdentitySurvivesDisplayNameAndRuntimeIdChanges()
    {
        var before = new TmuxSession("agent", "project/agent", "project", new WslTarget("host", "Before", IsSsh: true), "$1");
        var after = before with { Id = "$2", Target = before.Target with { DisplayName = "After" } };
        Assert.Equal(before.Saved.Key, after.Saved.Key);
        Assert.NotEqual(before.Saved.Key, (after with { Target = after.Target with { Distribution = "other-host" } }).Saved.Key);
    }
}
