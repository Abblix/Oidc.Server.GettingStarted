using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Authentication;

namespace McpServerApp;

/// <summary>
/// The SDK's MCP authentication handler, which serves the protected resource metadata, with challenges that also
/// name the scope the MCP endpoint needs.
/// </summary>
/// <remarks>
/// The SDK's own challenge carries only resource_metadata. The MCP authorization specification adds that servers
/// "SHOULD include a scope parameter in the WWW-Authenticate header", which clients then request first, and that a
/// token lacking a scope gets 403 with error="insufficient_scope" naming the scopes needed.
/// </remarks>
public sealed class ScopeChallengeHandler(
    IOptionsMonitor<McpAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : McpAuthenticationHandler(options, logger, encoder)
{
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{MetadataUri}\", scope=\"{Scope}\"";
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.Headers.WWWAuthenticate =
            $"Bearer error=\"insufficient_scope\", scope=\"{Scope}\", resource_metadata=\"{MetadataUri}\"";
        return Task.CompletedTask;
    }

    private Uri MetadataUri => Options.ResourceMetadataUri
        ?? throw new InvalidOperationException($"{nameof(McpAuthenticationOptions.ResourceMetadataUri)} is not set");

    private string Scope => string.Join(' ', Options.ResourceMetadata?.ScopesSupported ?? []);
}
