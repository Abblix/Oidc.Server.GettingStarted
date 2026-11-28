using System.Net;
using Abblix.Jwt;
using Abblix.Oidc;
using Abblix.Oidc.Server.AspNetCore;
using Abblix.Oidc.Server.Common.Configuration;
using Abblix.Oidc.Server.Common.Constants;
using Abblix.Oidc.Server.Common.Interfaces;
using Abblix.Oidc.Server.Features;
using Abblix.Oidc.Server.Features.ClientIdMetadataDocuments;
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

var issuer = HttpsUri(configuration["Provider:Issuer"], "Provider:Issuer");
var mcpResource = HttpsUri(configuration["McpServer:Resource"], "McpServer:Resource");
var mcpResourceName = configuration["McpServer:Name"]
                      ?? throw new InvalidOperationException("McpServer:Name is not configured");
var mcpResourceApplication = configuration["McpServer:Application"]
                             ?? throw new InvalidOperationException("McpServer:Application is not configured");
var readScope = configuration["McpServer:Scopes:Read"]
                ?? throw new InvalidOperationException("McpServer:Scopes:Read is not configured");
var writeScope = configuration["McpServer:Scopes:Write"]
                 ?? throw new InvalidOperationException("McpServer:Scopes:Write is not configured");
// The client identifier URLs whose documents this provider fetches; unset, it fetches any the draft allows
var allowedClientIdPrefixes = configuration.GetSection("ClientIdMetadataDocuments:AllowedClientIdPrefixes").Get<string[]>();

var userStore = new TestUserStore(
    new TestUser(
        Subject: "1234567890",
        Name: "John Doe",
        Email: "john.doe@example.com",
        Password: "Jd!2024$3cur3"));
builder.Services.AddSingleton(userStore);
builder.Services.AddSingleton<IUserInfoProvider>(userStore);

// Any application published under a trusted prefix can become a client by naming its metadata document, so the user
// approves each authorization on the consent page, which names the client, where its name comes from, and the host
// its redirect URI points to (the MCP specification asks the authorization server to display it).
// ConsentPageDecisions says why no approval is remembered.
builder.Services.AddSingleton<ConsentPageDecisions>();
builder.Services.AddSingleton<IUserConsentsProvider>(services => services.GetRequiredService<ConsentPageDecisions>());

var oidcServer = builder.Services.AddOidcServices(options =>
{
    // #region new-client-options
    options.NewClientOptions = new NewClientOptions
    {
        // These are the library's defaults, written out so the sample shows the policy: an MCP client acts for a user
        // through the authorization code flow, renewed by refresh tokens, so a metadata document asking for anything
        // else, client_credentials included (a token for the MCP server with no user behind it), gets only these.
        AllowedGrantTypes = [GrantTypes.AuthorizationCode, GrantTypes.RefreshToken],

        // Allowed gives MCP clients a refresh token without prompt=consent: they ask for offline_access without it,
        // and by default (ConsentRequired) such a request gets none (OpenID Connect Core 1.0, section 11). The user's
        // answer on the consent page stands in for prompt=consent, so the assistant keeps working after its access token expires.
        OfflineAccess = OfflineAccess.Allowed,

        // A client known by its metadata document may get a token for the task tracker and nothing else this provider
        // protects, whatever it names: which resources a client reaches is the provider's policy, not the client's
        AllowedResources = [mcpResource],
    };
    // #endregion

#pragma warning disable ABX9001 // Client ID Metadata Documents are experimental until the draft becomes an RFC
    // #region cimd-prefixes
    // A client names itself by the https URL of a metadata document it publishes, the only way a client becomes known
    // here (draft-ietf-oauth-client-id-metadata-document-03). With prefixes set, the provider fetches only documents under
    // them, so a URL of an untrusted vendor is refused without a request leaving the provider; the user still
    // approves every client on the consent page.
    options.ClientIdMetadataDocuments = new ClientIdMetadataDocumentOptions
    {
        AllowedClientIdPrefixes = allowedClientIdPrefixes,

        // McpClientApp serves its document on this machine, and the draft forbids fetching one from a special-use
        // address (section 10.7) except on a development machine whose authorization server is itself on loopback.
        // The library applies this only while the tenant's issuer is on loopback, and a Production host does not
        // start with it.
        DocumentAddresses = builder.Environment.IsDevelopment()
            ? ClientIdMetadataDocumentAddresses.AllowLoopback
            : ClientIdMetadataDocumentAddresses.PublicOnly,
    };
    // #endregion
#pragma warning restore ABX9001

    options.Tenants.Add(new TenantDefinition
    {
        Id = "main",
        Issuer = issuer,

        // No pre-registered clients, and no registration endpoint: every MCP client arrives by its metadata document
        Clients = [],

        LoginUri = new Uri("/Auth/Login", UriKind.Relative),

        // Where the provider sends the user while ConsentPageDecisions holds the request's consent pending
        ConsentUri = new Uri("/Auth/Consent", UriKind.Relative),

        // The task tracker's MCP server as a protected resource, with a scope for reading the employee's tasks and
        // one for changing them. A client naming it in the resource parameter gets an access token whose audience
        // is exactly this URI, which is what the MCP server checks.
        Resources =
        [
            new ResourceDefinition(
                mcpResource,
                new ScopeDefinition(readScope),
                new ScopeDefinition(writeScope)),
        ],

        SigningKeys = [JsonWebKeyFactory.CreateRsa(PublicKeyUsages.Signature)],
    });
});

#pragma warning disable ABX9001
// Resolves a client_id that is an https URL by fetching its document through the library's outbound guard, and
// advertises client_id_metadata_document_supported in the metadata. What such a client may ask for is bounded by
// NewClientOptions above.
oidcServer.AddClientIdMetadataDocuments();
#pragma warning restore ABX9001

builder.Services.AddAuthentication().AddCookie();

builder.Services.AddAuthorization();
builder.Services.AddAntiforgery();
// ConsentPageDecisions keeps each decision here until its request resumes
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Every page this provider renders - the login form, a consent or error page - acts for the user on a click, and a
// page on another site could load it in a nearly invisible frame over a decoy button. RFC 9700 section 4.16:
// "Authorization servers MUST prevent clickjacking attacks", with X-Frame-Options and, in addition, a Content
// Security Policy. Nothing this provider serves is meant to be framed, so every response forbids it.
app.Use((context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy = AntiFramingHeaders.ContentSecurityPolicy;
    context.Response.Headers.XFrameOptions = AntiFramingHeaders.XFrameOptions;
    return next(context);
});

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

    // The authorization endpoint already found the client before sending the user here, so the name is only looked up
    var clientName = (await clientInfoProvider.FindClientAsync(tenant, request.ClientId!)).TryGetSuccess(out var client)
        ? client.ClientName
        : null;
    return Results.Content(
        ConsentPage(
            antiforgery.GetAndStoreTokens(context),
            requestUri,
            request,
            clientName,
            mcpResource,
            $"{mcpResourceName} ({mcpResourceApplication})"),
        "text/html");
});

app.MapPost("/Auth/Consent", async (
        [FromForm] string requestUri,
        [FromForm] string decision,
        [FromForm(Name = "scope")] string[]? scopes,
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
        if (await requestStorage.TryGetAsync(tenant, storedUri) is not { } request)
            return Results.Content(ExpiredPage(), "text/html");

        // The values come from the browser, so anything but the two buttons' values, or a scope the request did not
        // ask for, is a bad request, not a fault. Allow with every scope cleared grants nothing, which is a refusal.
        var checkedScopes = scopes ?? [];
        if (!checkedScopes.All(scope => request.Scope.Contains(scope, StringComparer.Ordinal)))
            return Results.BadRequest();

        switch (decision)
        {
            case "allow" when checkedScopes.Length > 0:
                decisions.Record(tenant, authSession, storedUri, checkedScopes);
                break;

            case "allow" or "deny":
                decisions.Record(tenant, authSession, storedUri, grantedScopes: null);
                break;

            default:
                return Results.BadRequest();
        }

        // Back to the authorization endpoint, which resumes the request: a code for the scopes the user allowed,
        // access_denied when the user allowed none
        var authorizeUrl = new UriBuilder(endpointResolver.Resolve(OidcEndpoints.Authorize)!)
            { Query = { [AuthorizationRequest.Parameters.RequestUri] = storedUri.OriginalString } };
        return Results.Redirect(authorizeUrl);
    })
    // The handler checks the antiforgery token itself, as the login form does
    .DisableAntiforgery();

app.Run();

static string ConsentPage(
    AntiforgeryTokenSet tokens,
    Uri requestUri,
    AuthorizationRequest request,
    string? clientName,
    Uri mcpResource,
    string mcpResourceName)
{
    // Every value below was chosen by whoever published the client's metadata document, so each is encoded before it
    // reaches the page
    var client = WebUtility.HtmlEncode(clientName ?? request.ClientId ?? "");
    var clientId = WebUtility.HtmlEncode(request.ClientId ?? "");
    var redirectUri = WebUtility.HtmlEncode(request.RedirectUri?.OriginalString ?? "");

    // Every client of this provider is known by its metadata document, so its client_id is the document's https URL.
    // Its name is the document's claim; the host it is published on is the one thing the provider checked
    // (draft-ietf-oauth-client-id-metadata-document-03 section 10.10).
    var documentHost = WebUtility.HtmlEncode(new Uri(request.ClientId!).Authority);

    // The MCP specification: authorization servers "SHOULD display additional warnings for localhost-only redirect
    // URIs". Any program on the user's computer can listen on a loopback port, so the code may reach one the user
    // never meant to authorize.
    var loopbackWarning = request.RedirectUri is { IsLoopback: true }
        ? "<p><b>The code goes to a program on this computer.</b> Any program running on it could receive the code, " +
          "so allow only if you have just started this application yourself.</p>"
        : "";
    var redirectWhere = request.RedirectUri is { IsLoopback: true } ? ", a program on this computer" : "";

    // Each scope is granted on its own: the user may allow the application to read without letting it change
    var scopes = string.Join("", request.Scope.Select(scope =>
    {
        var value = WebUtility.HtmlEncode(scope);
        return $"""<li><label><input type="checkbox" name="scope" value="{value}" checked /> {value}</label></li>""";
    }));
    // The provider declares the resources it serves, so it can name them; the client names none of them here
    var resources = string.Join(", ", (request.Resources ?? []).Select(resource =>
        resource == mcpResource
            ? $"<b>{WebUtility.HtmlEncode(mcpResourceName)}</b>"
            : $"<code>{WebUtility.HtmlEncode(resource.OriginalString)}</code>"));
    return $"""
        <!DOCTYPE html>
        <html>
        <head>{PageHead()}</head>
        <body>
            <h1>Allow {client}?</h1>
            <p><b>{client}</b> asks to act for you.</p>
            <ul>
                <li>Asked by: the application <b>{client}</b>; its description, published at <b>{documentHost}</b>, names it so (client ID <code>{clientId}</code>)</li>
                <li>The code goes to: <code>{redirectUri}</code>, the redirect address {client} gave{redirectWhere}</li>
                <li>Access to: {resources}</li>
            </ul>
            {loopbackWarning}
            <form method="post" action="/Auth/Consent">
                <input type="hidden" name="{WebUtility.HtmlEncode(tokens.FormFieldName)}" value="{WebUtility.HtmlEncode(tokens.RequestToken)}" />
                <input type="hidden" name="requestUri" value="{WebUtility.HtmlEncode(requestUri.OriginalString)}" />
                <p>Scopes it may use (clear one to refuse it):</p>
                <ul>{scopes}</ul>
                <button type="submit" name="decision" value="allow">Allow</button>
                <button type="submit" name="decision" value="deny">Deny</button>
            </form>
        </body>
        </html>
        """;
}

// The head of every page: one centred column in the system font, as the assistant's page has
static string PageHead() => """
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>McpServerSample provider</title>
    <style>
    body { font-family: system-ui, sans-serif; max-width: 60rem; margin: 2rem auto; padding: 0 1rem; color: #222; background: #fff; }
    code { font-family: ui-monospace, monospace; }
    input[type=email], input[type=password] { font: inherit; padding: .3rem; }
    button { padding: .4rem .9rem; border: 1px solid #0b5cad; border-radius: .25rem; background: #0b5cad; color: #fff; font: inherit; cursor: pointer; }
    button[value=deny] { background: #fff; color: #0b5cad; }
    </style>
    """;

static string ExpiredPage() => $"""
    <!DOCTYPE html>
    <html>
    <head>{PageHead()}</head>
    <body>
        <h1>The request has expired</h1>
        <p>Start again from the MCP client.</p>
    </body>
    </html>
    """;

static Uri HttpsUri(string? value, string name)
    => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        ? uri
        : throw new InvalidOperationException(value is null
            ? $"{name} is not configured"
            : $"{name} is '{value}'; it has to be an absolute https URL");

static string LoginPage(AntiforgeryTokenSet tokens, string? requestUri, string? error)
{
    var encodedRequestUri = WebUtility.HtmlEncode(requestUri ?? "");
    var errorBlock = error == null ? "" : $"<p style=\"color:red\">{WebUtility.HtmlEncode(error)}</p>";
    return $"""
        <!DOCTYPE html>
        <html>
        <head>{PageHead()}</head>
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

static string SignedInPage() => $"""
    <!DOCTYPE html>
    <html>
    <head>{PageHead()}</head>
    <body>
        <h1>Signed in</h1>
        <p>Connect an MCP client to continue: it sends you here when it needs a sign-in.</p>
    </body>
    </html>
    """;
