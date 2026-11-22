using System.ComponentModel;
using System.Security.Claims;
using ModelContextProtocol.Server;

namespace McpServerApp;

/// <summary>
/// The one tool this server exposes. It answers from the access token, so a successful call shows that the
/// token the provider issued reached the tool with the user it was issued for.
/// </summary>
[McpServerToolType]
public sealed class WhoAmITool
{
    [McpServerTool(Name = "who_am_i"), Description("Returns the user, the client and the scope of the access token used for the call.")]
    public static string WhoAmI(ClaimsPrincipal user)
        => $"subject={user.FindFirstValue("sub")}; client_id={user.FindFirstValue("client_id")}; scope={user.FindFirstValue("scope")}";
}
