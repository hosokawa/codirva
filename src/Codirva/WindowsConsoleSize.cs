namespace Codirva;

internal sealed class WindowsConsoleSize(Action<int, int> start, Action<int, int> resize)
{
    private bool _started;

    public void Apply(int columns, int rows)
    {
        columns = Math.Clamp(columns, 1, 500);
        rows = Math.Clamp(rows, 1, 500);

        // Output written before the first resize would be replayed at the wrong width.
        if (!_started)
        {
            start(columns, rows);
            _started = true;
            return;
        }

        resize(columns, rows);
    }
}
