using System.Net;
using Abblix.DependencyInjection;
using Abblix.Jwt;
using Abblix.Oidc.Server.Common.Configuration;
using Abblix.Oidc.Server.Common.Constants;
using Abblix.Oidc.Server.Common.Interfaces;
using Abblix.Oidc.Server.Endpoints.DynamicClientManagement.Validation;
using Abblix.Oidc.Server.Features;
using Abblix.Oidc.Server.Features.ClientInformation;
using Abblix.Oidc.Server.Features.Consents;
using Abblix.Oidc.Server.Features.MultiTenancy;
using Abblix.Oidc.Server.Features.RandomGenerators;
using Abblix.Oidc.Server.Features.Storages;
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

// With open registration below any application can become a client, so the user approves each authorization on
// the consent page, which names the client and the host its redirect URI points to (the MCP specification asks
// the authorization server to display it). ConsentPageDecisions says why no approval is remembered.
builder.Services.AddSingleton<ConsentPageDecisions>();
builder.Services.AddSingleton<IUserConsentsProvider>(services => services.GetRequiredService<ConsentPageDecisions>());

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

        // Where the provider sends the user while ConsentPageDecisions holds the request's consent pending
        ConsentUri = new Uri("/Auth/Consent", UriKind.Relative),

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
        // A page on another site could submit this form with the attacker's own credentials: the victim's browser
        // would be signed in as the attacker, and the MCP client the victim then authorizes acts on the attacker's
        // account. The antiforgery token the form carries is what such a page cannot supply. A form opened in
        // another tab before signing in carries a token that no longer matches, so it is shown again.
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            var fresh = antiforgery.GetAndStoreTokens(context);
            return Results.Content(LoginPage(fresh, requestUri, "The sign-in form has expired, please sign in again"),
                "text/html");
        }

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
    })
    // The handler checks the antiforgery token itself, so an expired form gets the login page again
    // rather than the bare 400 the automatic check answers
    .DisableAntiforgery();

app.MapGet("/Auth/Consent", async (
    string? request_uri,
    IAntiforgery antiforgery,
    HttpContext context,
    IAuthSessionService authService,
    ITenantAccessor tenantAccessor,
    IAuthorizationRequestStorage requestStorage,
    IClientInfoProvider clientInfoProvider) =>
{
    if (!Uri.TryCreate(request_uri, UriKind.Absolute, out var requestUri) ||
        await authService.AuthenticateAsync() == null)
    {
        return Results.BadRequest();
    }

    var tenant = tenantAccessor.CurrentTenant();
    var request = await requestStorage.TryGetAsync(tenant, requestUri);
    if (request == null)
        return Results.Content(ExpiredPage(), "text/html");

    var client = await clientInfoProvider.TryFindClientAsync(tenant, request.ClientId!);
    return Results.Content(
        ConsentPage(antiforgery.GetAndStoreTokens(context), requestUri, request, client?.ClientName),
        "text/html");
});

app.MapPost("/Auth/Consent", async (
        [FromForm] string requestUri,
        [FromForm] string decision,
        IAntiforgery antiforgery,
        HttpContext context,
        IAuthSessionService authService,
        ITenantAccessor tenantAccessor,
        IAuthorizationRequestStorage requestStorage,
        IOidcEndpointResolver endpointResolver,
        ConsentPageDecisions decisions) =>
    {
        // A page on another site could submit this form for the user and approve a client the user never saw.
        // The antiforgery token is what such a page cannot supply.
        if (!await antiforgery.IsRequestValidAsync(context) ||
            !Uri.TryCreate(requestUri, UriKind.Absolute, out var storedUri) ||
            await authService.AuthenticateAsync() is not { } authSession)
        {
            return Results.BadRequest();
        }

        // Only a request the provider sent the user here with can be answered
        var tenant = tenantAccessor.CurrentTenant();
        if (await requestStorage.TryGetAsync(tenant, storedUri) == null)
            return Results.Content(ExpiredPage(), "text/html");

        // The value comes from the browser, so anything but the two buttons' values is a bad request, not a fault
        bool? granted = decision switch
        {
            "allow" => true,
            "deny" => false,
            _ => null,
        };
        if (granted == null)
            return Results.BadRequest();

        decisions.Record(tenant, authSession, storedUri, granted.Value);

        // Back to the authorization endpoint, which resumes the request: a code for the client when the user
        // allowed it, access_denied when not
        var authorizeUrl = new UriBuilder(endpointResolver.Resolve(OidcEndpoints.Authorize)!)
            { Query = { [AuthorizationRequest.Parameters.RequestUri] = storedUri.OriginalString } };
        return Results.Redirect(authorizeUrl);
    })
    // The handler checks the antiforgery token itself, as the login form does
    .DisableAntiforgery();

app.Run();

static string ConsentPage(AntiforgeryTokenSet tokens, Uri requestUri, AuthorizationRequest request, string? clientName)
{
    // Every value below was chosen by whoever registered the client, so each is encoded before it reaches the page
    var client = WebUtility.HtmlEncode(clientName ?? request.ClientId ?? "");
    var clientId = WebUtility.HtmlEncode(request.ClientId ?? "");
    var redirectUri = WebUtility.HtmlEncode(request.RedirectUri?.OriginalString ?? "");
    var redirectHost = WebUtility.HtmlEncode(request.RedirectUri?.Host ?? "");
    var scopes = string.Join("", request.Scope.Select(scope => $"<li>{WebUtility.HtmlEncode(scope)}</li>"));
    var resources = string.Join("", (request.Resources ?? [])
        .Select(resource => $"<li>{WebUtility.HtmlEncode(resource.OriginalString)}</li>"));
    return $"""
        <!DOCTYPE html>
        <html>
        <head><title>McpServerSample provider</title></head>
        <body>
            <h1>Allow {client}?</h1>
            <p>The application <b>{client}</b> (client ID <code>{clientId}</code>) asks to act for you. It registered
            itself with this provider, so the name is its own claim.</p>
            <p>If you allow it, the provider sends the authorization code to <b>{redirectHost}</b>: <code>{redirectUri}</code></p>
            <p>Scopes:</p>
            <ul>{scopes}</ul>
            <p>Resources:</p>
            <ul>{resources}</ul>
            <form method="post" action="/Auth/Consent">
                <input type="hidden" name="{WebUtility.HtmlEncode(tokens.FormFieldName)}" value="{WebUtility.HtmlEncode(tokens.RequestToken)}" />
                <input type="hidden" name="requestUri" value="{WebUtility.HtmlEncode(requestUri.OriginalString)}" />
                <button type="submit" name="decision" value="allow">Allow</button>
                <button type="submit" name="decision" value="deny">Deny</button>
            </form>
        </body>
        </html>
        """;
}

static string ExpiredPage() => """
    <!DOCTYPE html>
    <html>
    <head><title>McpServerSample provider</title></head>
    <body>
        <h1>The request has expired</h1>
        <p>Start again from the MCP client.</p>
    </body>
    </html>
    """;

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
