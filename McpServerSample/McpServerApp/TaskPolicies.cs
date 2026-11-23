namespace McpServerApp;

/// <summary>
/// The authorization policies the tools name: one for reading the user's tasks and one for changing them, each
/// satisfied by its own scope from the settings (Mcp:Scopes).
/// </summary>
public static class TaskPolicies
{
    public const string Read = "ReadTasks";
    public const string Write = "WriteTasks";
}
