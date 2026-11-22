using System.ComponentModel;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace McpServerApp;

/// <summary>
/// The tracker's tools. Each works on the tasks of the user the access token was issued for, read from its subject,
/// so an assistant acting for one employee never sees another's tasks. They answer with the tasks as structured
/// content, so a client reads an id without parsing text.
/// </summary>
[McpServerToolType]
public sealed class TaskTools(TaskTracker tracker)
{
    [McpServerTool(Name = "list_my_tasks", UseStructuredContent = true), Description("Lists the signed-in user's tasks.")]
    [Authorize(Policy = TaskPolicies.Read)]
    public IReadOnlyList<TrackedTask> ListMyTasks(ClaimsPrincipal user) => tracker.ListFor(Subject(user));

    [McpServerTool(Name = "create_task", UseStructuredContent = true), Description("Creates a task in the signed-in user's list.")]
    [Authorize(Policy = TaskPolicies.Write)]
    public TrackedTask CreateTask(ClaimsPrincipal user, [Description("What the task is about.")] string title)
        => tracker.Create(Subject(user), title);

    [McpServerTool(Name = "close_task", UseStructuredContent = true), Description("Closes one of the signed-in user's tasks.")]
    [Authorize(Policy = TaskPolicies.Write)]
    public TrackedTask CloseTask(
        ClaimsPrincipal user,
        [Description("The id of the task, as list_my_tasks shows it.")] int id)
        => tracker.Close(Subject(user), id) ?? throw new McpException($"You have no task {id}");

    // The JwtBearer events refuse a token without a subject before any tool runs
    private static string Subject(ClaimsPrincipal user) => user.FindFirstValue("sub")!;
}
