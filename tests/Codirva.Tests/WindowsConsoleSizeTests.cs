namespace Codirva.Tests;

public sealed class WindowsConsoleSizeTests
{
    [Fact]
    public void StartsAtTheFirstAttachedViewportSize()
    {
        var calls = new List<(string Operation, int Columns, int Rows)>();
        var size = new WindowsConsoleSize(
            (columns, rows) => calls.Add(("start", columns, rows)),
            (columns, rows) => calls.Add(("resize", columns, rows)));

        Assert.Empty(calls);
        size.Apply(153, 52);
        size.Apply(87, 26);

        Assert.Equal([("start", 153, 52), ("resize", 87, 26)], calls);
    }
}
