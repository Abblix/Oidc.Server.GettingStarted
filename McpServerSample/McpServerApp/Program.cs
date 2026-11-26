using McpServerApp;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

var resource = HttpsUri(configuration["Mcp:Resource"], "Mcp:Resource");
var authorizationServer = HttpsUri(configuration["Mcp:AuthorizationServer"], "Mcp:AuthorizationServer").OriginalString;
var resourceName = configuration["Mcp:ResourceName"]
                   ?? throw new InvalidOperationException("Mcp:ResourceName is not configured");
var readScope = configuration["Mcp:Scopes:Read"]
                ?? throw new InvalidOperationException("Mcp:Scopes:Read is not configured");
var writeScope = configuration["Mcp:Scopes:Write"]
                 ?? throw new InvalidOperationException("Mcp:Scopes:Write is not configured");
var seedTasks = configuration.GetSection("Tracker:SeedTasks").Get<string[]>()
                ?? throw new InvalidOperationException("Tracker:SeedTasks is not configured");

// RFC 9728 section 3: the metadata lives at the resource identifier with /.well-known/oauth-protected-resource
// inserted between the host and the path, so https://localhost:5008/mcp publishes it at
// https://localhost:5008/.well-known/oauth-protected-resource/mcp
var resourceMetadataUri = new Uri(resource, "/.well-known/oauth-protected-resource" + resource.AbsolutePath);

var authentication = builder.Services.AddAuthentication(options =>
{
    // The token is validated by JwtBearer; a missing or rejected one is answered by the MCP handler, whose
    // challenge tells the client where the protected resource metadata is
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
});

// #region jwt-bearer
authentication.AddJwtBearer(options =>
{
    // The signing keys and the issuer come from the provider's metadata
    options.Authority = authorizationServer;

    // The MCP specification: servers "MUST validate that access tokens were issued specifically for them as
    // the intended audience". The provider writes the resource the client named (RFC 8707) into aud, so a
    // token issued for any other resource, or for none, is refused here.
    options.Audience = resource.OriginalString;

    // A refresh token of this provider is addressed to the issuer, so the audience check already refuses one; the
    // type check keeps that true whatever audience a token carries, and the MCP specification says servers
    // "MUST NOT accept or transit any other tokens".
    options.TokenValidationParameters.ValidTypes = ["at+jwt"];

    // Keep the claims under their JWT names (sub, scope) rather than the mapped .NET claim types
    options.MapInboundClaims = false;

    // The tools act for a user, so a token issued to a client on its own behalf is refused like any other token
    // not meant for this server. The provider marks such a token with idp=client_credentials; its subject is no
    // guide, since a client with pairwise subjects gets a pseudonym there rather than its client_id.
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            var subject = context.Principal?.FindFirst("sub")?.Value;
            var identityProvider = context.Principal?.FindFirst("idp")?.Value;
            if (string.IsNullOrEmpty(subject) || identityProvider == "client_credentials")
                context.Fail("The access token carries no user");

            return Task.CompletedTask;
        },
    };
});
// #endregion

authentication.AddScheme<McpAuthenticationOptions, ScopeChallengeHandler>(
    McpAuthenticationDefaults.AuthenticationScheme,
    McpAuthenticationDefaults.DisplayName,
    options =>
    {
        options.ResourceMetadataUri = resourceMetadataUri;
        options.ResourceMetadata = new()
        {
            Resource = resource.OriginalString,
            AuthorizationServers = { authorizationServer },
            // The MCP specification: scopes_supported "is intended to represent the minimal set of scopes
            // necessary for basic functionality", with further scopes requested through step-up. A client starts
            // with reading; the first call that changes a task is answered 403 naming tasks.write (ToolScopeGate),
            // and the client asks the user for it then, so a token that only ever reads never carries it.
            ScopesSupported = [readScope],
            ResourceName = resourceName,
        };
    });

// #region scope-policies
// Reading the tracker and changing it are granted separately, so an employee can let an assistant see the tasks
// without letting it change them. Each tool names the policy it needs.
// The scope behind each policy: the policies are built from it, and a refused call is told which one it lacks.
var policyScopes = new Dictionary<string, string>
{
    [TaskPolicies.Read] = readScope,
    [TaskPolicies.Write] = writeScope,
};
var authorizationBuilder = builder.Services.AddAuthorizationBuilder();
foreach (var (policyName, policyScope) in policyScopes)
{
    authorizationBuilder.AddPolicy(policyName, policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(context => context.User.FindFirst("scope")?.Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(policyScope, StringComparer.Ordinal) is true));
}
// #endregion

builder.Services.AddSingleton(new TaskTracker(seedTasks));

builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    // Without the SDK's authorization filters every tool is listed whatever the token's scope, and ToolScopeGate
    // refuses a call the scope does not cover (ToolPolicyAttribute says why)
    .WithTools<TaskTools>()
    .WithTools<WhoAmITool>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// #region map-mcp
// After the endpoint's own check that a valid token came with the request: a call to a tool the token's scope does
// not cover is answered 403 with the scope it needs
var gate = ActivatorUtilities.CreateInstance<ToolScopeGate>(app.Services, policyScopes);
app.Use(gate.InvokeAsync);

// The gate acts on the endpoint carrying it, so every path spelling routing sends here (/mcp/ included) passes it
app.MapMcp(resource.AbsolutePath).RequireAuthorization().WithMetadata(gate);
// #endregion

app.Run();

static Uri HttpsUri(string? value, string name)
    => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        ? uri
        : throw new InvalidOperationException(value is null
            ? $"{name} is not configured"
            : $"{name} is '{value}'; it has to be an absolute https URL");
