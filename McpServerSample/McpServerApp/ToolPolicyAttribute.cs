namespace McpServerApp;

/// <summary>
/// Names the authorization policy a tool needs; <see cref="ToolScopeGate"/> reads it from the tool's metadata and
/// refuses a call whose token fails it.
/// </summary>
/// <remarks>
/// Not [Authorize]: the SDK's authorization filters, which the SDK requires for a tool carrying it, leave a tool the
/// token fails out of tools/list. An assistant offers its model only the listed tools, so it would never call the
/// tool, never be refused with the scope it lacks, and never ask the user for that scope.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ToolPolicyAttribute(string policy) : Attribute
{
    public string Policy { get; } = policy;
}
