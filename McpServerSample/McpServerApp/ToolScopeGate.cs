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
/// The gate runs on the endpoint MapMcp maps it to, whatever path spelling routed there, reads the message with the
/// SDK's own message types, and takes the policy each tool needs from the tool's <see cref="ToolPolicyAttribute"/> in
/// the SDK's own registry. It is the only check of a tool's policy: the SDK's filters are off, so tools/list names
/// every tool.
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
        if (context.GetEndpoint()?.Metadata.GetMetadata<ToolScopeGate>() is null ||
            !HttpMethods.IsPost(context.Request.Method))
        {
            await next(context);
            return;
        }

        // A body the gate cannot read is refused here rather than handed on: the SDK reads such a body from where the
        // failed read stopped, so it could find a call the gate never saw
        var (readable, tool) = await ReadCalledToolAsync(context.Request);
        if (!readable)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (tool is not null &&
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
    /// Whether the body is a JSON-RPC message, read as the SDK reads it, in the charset Content-Type names, with its
    /// message types and its protocol serializer options, which match property names case-sensitively as the SDK's
    /// dispatch does; and the tool it names when it is a tools/call request.
    /// </summary>
    private static async Task<(bool Readable, string? Tool)> ReadCalledToolAsync(HttpRequest request)
    {
        request.EnableBuffering();
        try
        {
            var message = await request.ReadFromJsonAsync<JsonRpcMessage>(
                McpJsonUtilities.ProtocolOptions, request.HttpContext.RequestAborted);

            return message switch
            {
                null => (false, null),
                JsonRpcRequest { Method: RequestMethods.ToolsCall, Params: { } parameters } =>
                    (true, parameters.Deserialize<CallToolRequestParams>(McpJsonUtilities.ProtocolOptions)?.Name),
                _ => (true, null),
            };
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException or
                                              InvalidOperationException)
        {
            // No JSON content type, a charset no encoding answers to, a body that is not one JSON-RPC message, or
            // tools/call parameters it cannot read
            return (false, null);
        }
        finally
        {
            request.Body.Position = 0;
        }
    }
}
