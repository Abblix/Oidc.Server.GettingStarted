namespace McpServerApp;

/// <summary>
/// One task in a user's list.
/// </summary>
public sealed record TrackedTask(int Id, string Title, bool Closed);
