using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Protocol;
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
/// The gate runs on the endpoint MapMcp maps it to, whatever path spelling routed there, reads the request the way the
/// SDK reads it, and takes the policy each tool needs from the tool's <see cref="ToolPolicyAttribute"/> in the SDK's
/// own registry. It is the only check of a tool's policy: the SDK's filters are off, so tools/list names every tool.
/// </remarks>
public sealed class ToolScopeGate(
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
        if (context.GetEndpoint()?.Metadata.GetMetadata<ToolScopeGate>() is not null &&
            HttpMethods.IsPost(context.Request.Method) &&
            await CalledToolAsync(context.Request) is { } tool &&
            serverOptions.Value.ToolCollection?.TryGetPrimitive(tool, out var serverTool) is true)
        {
            foreach (var policy in serverTool.Metadata.OfType<ToolPolicyAttribute>().Select(attribute => attribute.Policy))
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
    /// The tool a tools/call request names, read with the SDK's own message types and serializer options so the gate
    /// and the dispatcher read the same name; null for any other request, which the body is left for the SDK to answer.
    /// </summary>
    private static async Task<string?> CalledToolAsync(HttpRequest request)
    {
        request.EnableBuffering();
        try
        {
            var message = await JsonSerializer.DeserializeAsync<JsonRpcMessage>(
                request.Body, McpJsonUtilities.DefaultOptions, request.HttpContext.RequestAborted);

            return message is JsonRpcRequest { Method: RequestMethods.ToolsCall, Params: { } parameters }
                ? parameters.Deserialize<CallToolRequestParams>(McpJsonUtilities.DefaultOptions)?.Name
                : null;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
        {
            // A body the SDK's serializer cannot read: the SDK answers it with its own error
            return null;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }
}
