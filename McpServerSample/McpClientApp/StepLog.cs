namespace McpClientApp;

/// <summary>
/// The steps of the browser run as the assistant's page shows them, latest first, each also printed to the terminal.
/// </summary>
public sealed class StepLog
{
    private readonly Lock _lock = new();
    private readonly List<(DateTimeOffset At, string Text)> _steps = [];

    public void Add(string text)
    {
        Console.WriteLine(text);
        lock (_lock)
            _steps.Add((DateTimeOffset.UtcNow, text));
    }

    public IReadOnlyList<(DateTimeOffset At, string Text)> LatestFirst()
    {
        lock (_lock)
            return _steps.AsEnumerable().Reverse().ToList();
    }
}
