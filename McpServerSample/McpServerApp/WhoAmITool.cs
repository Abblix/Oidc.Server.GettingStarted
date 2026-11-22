using System.ComponentModel;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace McpServerApp;

/// <summary>
/// Answers from the access token, so a call shows the user the provider signed in, the client it issued the token
/// to, and the scope the user granted that client.
/// </summary>
[McpServerToolType]
public sealed class WhoAmITool
{
    [McpServerTool(Name = "who_am_i"), Description("Returns the user, the client and the scope of the access token used for the call.")]
    [Authorize(Policy = TaskPolicies.Read)]
    public static string WhoAmI(ClaimsPrincipal user)
        => $"subject={user.FindFirstValue("sub")}; client_id={user.FindFirstValue("client_id")}; scope={user.FindFirstValue("scope")}";
}
