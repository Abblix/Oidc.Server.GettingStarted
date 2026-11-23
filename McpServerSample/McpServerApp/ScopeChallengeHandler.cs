using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // RFC 6750 section 3: a request that "included an access token and failed authentication" SHOULD be told
        // why, while a request without a token gets no error code. The JwtBearer result is the one already
        // computed for this request.
        var authentication = await Context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        var error = authentication.Failure is null ? "" : "error=\"invalid_token\", ";

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"Bearer {error}resource_metadata=\"{MetadataUri}\", scope=\"{Scope}\"";
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        // The specification: "servers SHOULD include the scopes needed to satisfy the current operation in the scope
        // parameter". ToolScopeGate names the scope of the tool called; any other refusal names every scope.
        var scope = properties.Items.TryGetValue(ToolScopeGate.ScopeProperty, out var needed) && needed is not null
            ? needed
            : Scope;

        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.Headers.WWWAuthenticate =
            $"Bearer error=\"insufficient_scope\", scope=\"{scope}\", resource_metadata=\"{MetadataUri}\"";
        return Task.CompletedTask;
    }

    private Uri MetadataUri => Options.ResourceMetadataUri
        ?? throw new InvalidOperationException($"{nameof(McpAuthenticationOptions.ResourceMetadataUri)} is not set");

    private string Scope => string.Join(' ', Options.ResourceMetadata?.ScopesSupported ?? []);
}
