using System.Net;
using Abblix.DependencyInjection;
using Abblix.Jwt;
using Abblix.Oidc.Server.Common.Configuration;
using Abblix.Oidc.Server.Common.Constants;
using Abblix.Oidc.Server.Common.Interfaces;
using Abblix.Oidc.Server.Endpoints.DynamicClientManagement.Validation;
using Abblix.Oidc.Server.Features;
using Abblix.Oidc.Server.Features.Consents;
using Abblix.Oidc.Server.Features.MultiTenancy;
using Abblix.Oidc.Server.Features.RandomGenerators;
using Abblix.Oidc.Server.Features.UserAuthentication;
using Abblix.Oidc.Server.Features.UserInfo;
using Abblix.Oidc.Server.MinimalApi;
using Abblix.Oidc.Server.Model;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using ProviderApp;
using UriBuilder = Abblix.Utils.UriBuilder;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

var issuer = configuration["Provider:Issuer"]
             ?? throw new InvalidOperationException("Provider:Issuer is not configured");
var mcpResource = configuration["McpServer:Resource"]
                  ?? throw new InvalidOperationException("McpServer:Resource is not configured");
var mcpScope = configuration["McpServer:Scope"]
               ?? throw new InvalidOperationException("McpServer:Scope is not configured");

var userStore = new TestUserStore(
    new TestUser(
        Subject: "1234567890",
        Name: "John Doe",
        Email: "john.doe@example.com",
        Password: "Jd!2024$3cur3"));
builder.Services.AddSingleton(userStore);
builder.Services.AddSingleton<IUserInfoProvider>(userStore);

// Grants every client everything it asks for once the user has signed in. With open registration below, any
// application can become a client, so a real deployment registers its own IUserConsentsProvider and shows the
// user which client is asking (the MCP specification asks the authorization server to display the redirect
// URI's host during authorization).
builder.Services.AddConsentGrantedForEveryClient();

builder.Services.AddOidcServices(options =>
{
    // MCP clients have no relationship with this provider before they connect, so they register themselves
    // (RFC 7591). Registration without an initial access token is what lets them do that unattended; the
    // library's default asks for one.
    options.RequireInitialAccessToken = false;

    options.Tenants.Add(new TenantDefinition
    {
        Id = "main",
        Issuer = new Uri(issuer),

        // No pre-registered clients: every MCP client arrives through dynamic registration
        Clients = [],

        LoginUri = new Uri("/Auth/Login", UriKind.Relative),

        // MCP clients register with the scope the MCP server's challenge names, and the registration endpoint
        // knows only the tenant's own scopes, not those of its resources: declared on the resource alone,
        // mcp:tools is refused at registration as an unknown scope.
        Scopes = [new ScopeDefinition(mcpScope)],

        // The MCP server as a protected resource. A client naming it in the resource parameter gets an access
        // token whose audience is exactly this URI, which is what the MCP server checks.
        Resources =
        [
            new ResourceDefinition(new Uri(mcpResource, UriKind.Absolute), new ScopeDefinition(mcpScope)),
        ],

        SigningKeys = [JsonWebKeyFactory.CreateRsa(PublicKeyUsages.Signature)],
    });
});

// Serves the registration endpoint and advertises it as registration_endpoint in the metadata; without this
// call the provider has no registration endpoint at all.
builder.Services.AddDynamicClientRegistration();

// Open registration admits any grant type the server supports, client_credentials included, which would get a
// token for the MCP server with no user behind it. This step, added to the end of the library's registration
// pipeline, admits only the grants an MCP client acting for a user needs.
builder.Services.Decompose<IClientRegistrationContextValidator>()
    .AddLast(ServiceDescriptor.Singleton<IClientRegistrationContextValidator, UserGrantsOnlyValidator>());

builder.Services.AddAuthentication().AddCookie();

builder.Services.AddAuthorization();
builder.Services.AddAntiforgery();
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

app.UseRouting();
app.UseCors();
app.UseAuthorization();

// A page on another site could submit the login form with the attacker's own credentials: the victim's browser
// would be signed in as the attacker, and the MCP client the victim then authorizes acts on the attacker's
// account. This middleware refuses a form post to /Auth/Login without the token only the login page hands out.
app.UseAntiforgery();

app.MapOidcEndpoints();

app.MapGet("/Auth/Login", (string? request_uri, IAntiforgery antiforgery, HttpContext context) =>
    Results.Content(LoginPage(antiforgery.GetAndStoreTokens(context), request_uri, error: null), "text/html"));

app.MapPost("/Auth/Login", async (
        [FromForm] string email,
        [FromForm] string password,
        [FromForm] string? requestUri,
        IAntiforgery antiforgery,
        HttpContext context,
        IAuthSessionService authService,
        IOidcEndpointResolver endpointResolver,
        ISessionIdGenerator sessionIdGenerator,
        TimeProvider timeProvider,
        TestUserStore userStore) =>
    {
        if (!userStore.TryAuthenticate(email, password, out var subject))
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Content(LoginPage(tokens, requestUri, "Invalid username or password"), "text/html");
        }

        var authSession = new AuthSession(
            subject,
            sessionIdGenerator.GenerateSessionId(),
            timeProvider.GetUtcNow(),
            CookieAuthenticationDefaults.AuthenticationScheme);

        await authService.SignInAsync(authSession);

        // A visit to the login page on its own carries no authorization request to resume, and this host has no
        // page of its own to return to
        if (string.IsNullOrEmpty(requestUri))
            return Results.Content(SignedInPage(), "text/html");

        // Back to the authorization endpoint, which resumes the pending request and redirects to the MCP client
        var authorizeUrl = new UriBuilder(endpointResolver.Resolve(OidcEndpoints.Authorize)!)
            { Query = { [AuthorizationRequest.Parameters.RequestUri] = requestUri } };
        return Results.Redirect(authorizeUrl);
    });

app.Run();

static string LoginPage(AntiforgeryTokenSet tokens, string? requestUri, string? error)
{
    var encodedRequestUri = WebUtility.HtmlEncode(requestUri ?? "");
    var errorBlock = error == null ? "" : $"<p style=\"color:red\">{WebUtility.HtmlEncode(error)}</p>";
    return $"""
        <!DOCTYPE html>
        <html>
        <head><title>McpServerSample provider</title></head>
        <body>
            <h1>Login</h1>
            {errorBlock}
            <form method="post" action="/Auth/Login">
                <input type="hidden" name="{WebUtility.HtmlEncode(tokens.FormFieldName)}" value="{WebUtility.HtmlEncode(tokens.RequestToken)}" />
                <input type="hidden" name="requestUri" value="{encodedRequestUri}" />
                <p><label>Email: <input type="email" name="email" autofocus /></label></p>
                <p><label>Password: <input type="password" name="password" /></label></p>
                <button type="submit">Sign in</button>
            </form>
        </body>
        </html>
        """;
}

static string SignedInPage() => """
    <!DOCTYPE html>
    <html>
    <head><title>McpServerSample provider</title></head>
    <body>
        <h1>Signed in</h1>
        <p>Connect an MCP client to continue: it sends you here when it needs a sign-in.</p>
    </body>
    </html>
    """;
