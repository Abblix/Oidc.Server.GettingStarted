using McpServerApp;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

var resource = new Uri(configuration["Mcp:Resource"]!);
var authorizationServer = configuration["Mcp:AuthorizationServer"]!;
var scope = configuration["Mcp:Scope"]!;

// RFC 9728 section 3: the metadata lives at the resource identifier with /.well-known/oauth-protected-resource
// inserted between the host and the path, so https://localhost:5008/mcp publishes it at
// https://localhost:5008/.well-known/oauth-protected-resource/mcp
var resourceMetadataUri = new Uri(resource, "/.well-known/oauth-protected-resource" + resource.AbsolutePath);

builder.Services
    .AddAuthentication(options =>
    {
        // The token is validated by JwtBearer; a missing or rejected one is answered by the MCP handler, whose
        // challenge tells the client where the protected resource metadata is
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        // The signing keys and the issuer come from the provider's metadata
        options.Authority = authorizationServer;

        // The MCP specification: servers "MUST validate that access tokens were issued specifically for them as
        // the intended audience". The provider writes the resource the client named (RFC 8707) into aud, so a
        // token issued for any other resource, or for none, is refused here.
        options.Audience = resource.OriginalString;

        // The provider gives a refresh token the same audience and signing key as the access token, so only the
        // JWT type tells them apart. Without this the MCP server would take a refresh token as a bearer token,
        // and the specification says MCP servers "MUST NOT accept or transit any other tokens".
        options.TokenValidationParameters.ValidTypes = ["at+jwt"];

        // Keep the claims under their JWT names (sub, scope) rather than the mapped .NET claim types
        options.MapInboundClaims = false;
    })
    .AddScheme<McpAuthenticationOptions, ScopeChallengeHandler>(
        McpAuthenticationDefaults.AuthenticationScheme,
        McpAuthenticationDefaults.DisplayName,
        options =>
        {
            options.ResourceMetadataUri = resourceMetadataUri;
            options.ResourceMetadata = new()
            {
                Resource = resource.OriginalString,
                AuthorizationServers = { authorizationServer },
                ScopesSupported = [scope],
                ResourceName = "McpServerSample",
            };
        });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("McpTools", policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(context => context.User.FindFirst("scope")?.Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(scope, StringComparer.Ordinal) is true));

builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithTools<WhoAmITool>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapMcp(resource.AbsolutePath).RequireAuthorization("McpTools");

app.Run();
