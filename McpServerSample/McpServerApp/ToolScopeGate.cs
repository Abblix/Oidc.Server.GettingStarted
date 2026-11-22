using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Server;

namespace McpServerApp;

/// <summary>
/// Answers a call to a tool whose policy the token does not meet with HTTP 403 and error="insufficient_scope" naming
/// the scope that tool needs, before the SDK dispatches the call.
/// </summary>
/// <remarks>
/// The MCP authorization specification has a server answer a token lacking a scope with "HTTP 403 Forbidden" and a
/// WWW-Authenticate header carrying error="insufficient_scope" and the scope "needed to satisfy the current operation",
/// which a client acting for a user answers by asking the user for that scope (step-up authorization). The SDK's own
/// authorization filters refuse such a call inside the JSON-RPC response instead, which a client cannot step up from.
/// The policy each tool needs is the one its [Authorize] attribute names, read from the SDK's own tool registry, and
/// the SDK's filters stay on behind this gate.
/// </remarks>
public sealed class ToolScopeGate(
    PathString mcpPath,
    IReadOnlyDictionary<string, string> policyScopes,
    IOptions<McpServerOptions> serverOptions,
    IAuthorizationService authorization)
{
    /// <summary>
    /// The authentication property the MCP handler's 403 challenge reads the needed scope from.
    /// </summary>
    public const string ScopeProperty = "scope";

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Path == mcpPath && HttpMethods.IsPost(context.Request.Method) &&
            await CalledToolAsync(context.Request) is { } tool &&
            serverOptions.Value.ToolCollection?.TryGetPrimitive(tool, out var serverTool) is true)
        {
            foreach (var policy in serverTool.Metadata.OfType<AuthorizeAttribute>().Select(a => a.Policy).OfType<string>())
            {
                if ((await authorization.AuthorizeAsync(context.User, policy)).Succeeded)
                    continue;

                var properties = new AuthenticationProperties { Items = { [ScopeProperty] = policyScopes[policy] } };
                await context.ForbidAsync(McpAuthenticationDefaults.AuthenticationScheme, properties);
                return;
            }
        }

        await next(context);
    }

    /// <summary>
    /// The tool a tools/call request names, or null for any other request; the body is left for the SDK to read again.
    /// </summary>
    private static async Task<string?> CalledToolAsync(HttpRequest request)
    {
        request.EnableBuffering();
        try
        {
            var message = await JsonNode.ParseAsync(request.Body);
            return message is JsonObject call &&
                   call["method"] is JsonValue method && method.TryGetValue<string>(out var name) &&
                   name == "tools/call" &&
                   call["params"]?["name"] is JsonValue toolName && toolName.TryGetValue<string>(out var tool)
                ? tool
                : null;
        }
        catch (JsonException)
        {
            // Not JSON: the SDK answers it with its own parse error
            return null;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }
}
