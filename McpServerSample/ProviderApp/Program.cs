using System.Net;
using System.Threading.RateLimiting;
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
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration.Json;
using ProviderApp;
using UriBuilder = Abblix.Utils.UriBuilder;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

var issuer = HttpsUri(configuration["Provider:Issuer"], "Provider:Issuer");
var mcpResource = HttpsUri(configuration["McpServer:Resource"], "McpServer:Resource");
var readScope = configuration["McpServer:Scopes:Read"]
                ?? throw new InvalidOperationException("McpServer:Scopes:Read is not configured");
var writeScope = configuration["McpServer:Scopes:Write"]
                 ?? throw new InvalidOperationException("McpServer:Scopes:Write is not configured");
// The client identifier URLs whose documents this provider fetches; unset, it fetches any the draft allows
var allowedClientIdPrefixes = configuration.GetSection("ClientIdMetadataDocuments:AllowedClientIdPrefixes").Get<string[]>();

const string clientSecretEncryptionKeySetting = "Provider:ClientSecretEncryptionKey";
var clientSecretEncryptionKey = configuration[clientSecretEncryptionKeySetting];
if (string.IsNullOrWhiteSpace(clientSecretEncryptionKey))
{
    throw new InvalidOperationException(
        $"{clientSecretEncryptionKeySetting} is not configured: set it to 32 random bytes, base64");
}

// The key in appsettings.json is published with the sample, so it keeps the stored secrets from nobody. It serves
// development alone: anywhere else the value has to come from a source of the deployment's own, an environment
// variable or a secret store, and the last provider holding the setting is the one whose value is used.
var keySource = ((IConfigurationRoot)configuration).Providers.Last(provider => provider.TryGet(clientSecretEncryptionKeySetting, out _));
if (!builder.Environment.IsDevelopment() && keySource is JsonConfigurationProvider { Source.Path: "appsettings.json" })
{
    throw new InvalidOperationException(
        $"{clientSecretEncryptionKeySetting} comes from appsettings.json, which holds the key published with the " +
        "sample and serves development only. Set your own, 32 random bytes in base64, through an environment " +
        "variable or a secret store.");
}

// Open registration lets anyone create clients, so each address may register only so many in a window
const string RegistrationRateLimit = "registration";
var registrationPermits = configuration.GetValue<int?>("Registration:PermitLimit") is { } permits and > 0
    ? permits
    : throw new InvalidOperationException("Registration:PermitLimit has to be a positive number of registrations");
var registrationWindow = TimeSpan.TryParse(configuration["Registration:Window"], out var window) && window > TimeSpan.Zero
    ? window
    : throw new InvalidOperationException("Registration:Window has to be a positive time span such as 00:01:00");

var userStore = new TestUserStore(
    new TestUser(
        Subject: "1234567890",
        Name: "John Doe",
        Email: "john.doe@example.com",
        Password: "Jd!2024$3cur3"));
builder.Services.AddSingleton(userStore);
builder.Services.AddSingleton<IUserInfoProvider>(userStore);

// Any application can become a client, by registering itself or by naming its metadata document, so the user
// approves each authorization on the consent page, which names the client, where its name comes from, and the host
// its redirect URI points to (the MCP specification asks the authorization server to display it).
// ConsentPageDecisions says why no approval is remembered.
builder.Services.AddSingleton<ConsentPageDecisions>();
builder.Services.AddSingleton<IUserConsentsProvider>(services => services.GetRequiredService<ConsentPageDecisions>());

var oidcServer = builder.Services.AddOidcServices(options =>
{
    // MCP clients have no relationship with this provider before they connect, so a client without a metadata
    // document registers itself (RFC 7591). Registration without an initial access token is what lets it do that
    // unattended; the library's default asks for one.
    options.RequireInitialAccessToken = false;

    options.NewClientOptions = new NewClientOptions
    {
        // Open registration would otherwise admit any grant type the server supports, client_credentials included,
        // which gets a token for the MCP server with no user behind it. An MCP client acts for a user through the
        // authorization code flow, renewed by refresh tokens, so a registration asking for anything else is refused.
        AllowedGrantTypes = [GrantTypes.AuthorizationCode, GrantTypes.RefreshToken],

        // An MCP client asks for offline_access to keep working after its access token expires, usually without
        // prompt=consent, and a request without it has that scope ignored (OpenID Connect Core 1.0 section 11). This
        // provider states for every client that registers that the user's consent, which the consent provider above
        // reports, is enough, so the client gets its refresh token.
        OfflineAccess = OfflineAccess.Allowed,
    };

#pragma warning disable ABX9001 // Client ID Metadata Documents are experimental until the draft becomes an RFC
    // A client may name itself by the https URL of a metadata document it publishes instead of registering
    // (draft-ietf-oauth-client-id-metadata-document-03). With prefixes set, the provider fetches only documents under
    // them, so a URL of an untrusted vendor is refused without a request leaving the provider; the user still
    // approves every client on the consent page.
    options.ClientIdMetadataDocuments.AllowedClientIdPrefixes = allowedClientIdPrefixes;

    // McpClientApp serves its document on this machine, and the draft forbids fetching one from a special-use address
    // (section 10.7) except on a development machine whose authorization server is itself on loopback. The library
    // applies this only while the tenant's issuer is on loopback, and a Production host does not start with it.
    if (builder.Environment.IsDevelopment())
        options.ClientIdMetadataDocuments.DocumentAddresses = ClientIdMetadataDocumentAddresses.AllowLoopback;
#pragma warning restore ABX9001

    options.Tenants.Add(new TenantDefinition
    {
        Id = "main",
        Issuer = issuer,

        // No pre-registered clients: every MCP client arrives by its metadata document or by dynamic registration
        Clients = [],

        LoginUri = new Uri("/Auth/Login", UriKind.Relative),

        // Where the provider sends the user while ConsentPageDecisions holds the request's consent pending
        ConsentUri = new Uri("/Auth/Consent", UriKind.Relative),

        // The task tracker's MCP server as a protected resource, with a scope for reading the employee's tasks and
        // one for changing them. A client naming it in the resource parameter gets an access token whose audience
        // is exactly this URI, which is what the MCP server checks. Registration accepts a scope a resource declares.
        Resources =
        [
            new ResourceDefinition(
                mcpResource,
                new ScopeDefinition(readScope),
                new ScopeDefinition(writeScope)),
        ],

        SigningKeys = [JsonWebKeyFactory.CreateRsa(PublicKeyUsages.Signature)],

        // The key each issued client_secret is also kept encrypted with, so a client reading its registration
        // (RFC 7592 section 3) gets its secret back. A tenant serving registration does not start without one.
        ClientSecretEncryption = new ClientSecretEncryptionSettings { Key = clientSecretEncryptionKey },
    });
});

#pragma warning disable ABX9001
// Resolves a client_id that is an https URL by fetching its document through the library's outbound guard, and
// advertises client_id_metadata_document_supported in the metadata. What such a client may ask for is bounded by
// NewClientOptions above, as for a client that registers itself.
oidcServer.AddClientIdMetadataDocuments();
#pragma warning restore ABX9001

// Serves the registration endpoint and advertises it as registration_endpoint in the metadata; without this
// call the provider has no registration endpoint at all.
builder.Services.AddDynamicClientRegistration();

builder.Services.AddAuthentication().AddCookie();

builder.Services.AddAuthorization();
builder.Services.AddAntiforgery();
// ConsentPageDecisions keeps each decision here until its request resumes
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpContextAccessor();

// Registrations are counted per client address; every other OIDC endpoint passes unlimited. The registration endpoint
// is recognised by the address the library publishes for it.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RegistrationRateLimit, context =>
    {
        var registration = context.RequestServices.GetRequiredService<IOidcEndpointResolver>()
            .Resolve(OidcEndpoints.RegisterClient)!;

        // The published address includes the path base the host may be mounted under, so the request's does too
        var path = context.Request.PathBase.Add(context.Request.Path);
        return HttpMethods.IsPost(context.Request.Method) && path == registration.AbsolutePath
            ? RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = registrationPermits, Window = registrationWindow })
            : RateLimitPartition.GetNoLimiter("");
    });
});

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
app.UseRateLimiter();
app.UseCors();
app.UseAuthorization();

app.MapOidcEndpoints().RequireRateLimiting(RegistrationRateLimit);

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
        ConsentPage(antiforgery.GetAndStoreTokens(context), requestUri, request, clientName),
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

static string ConsentPage(AntiforgeryTokenSet tokens, Uri requestUri, AuthorizationRequest request, string? clientName)
{
    // Every value below was chosen by whoever registered the client, so each is encoded before it reaches the page
    var client = WebUtility.HtmlEncode(clientName ?? request.ClientId ?? "");
    var clientId = WebUtility.HtmlEncode(request.ClientId ?? "");
    var redirectUri = WebUtility.HtmlEncode(request.RedirectUri?.OriginalString ?? "");
    var redirectHost = WebUtility.HtmlEncode(request.RedirectUri?.Host ?? "");

    // The provider assigns the client_id of every client that registers, so no registered client has one that is an
    // https URL, and a client_id written as one is the address of a metadata document. Its name is the document's
    // claim; the host it is published on is the one thing the provider checked (draft-ietf-oauth-client-id-metadata-
    // document-03 section 10.10). The URL is recognised the way the library recognises a client identifier URL,
    // character for character.
    var origin = request.ClientId is { } id &&
                 id.StartsWith("https://", StringComparison.Ordinal) &&
                 Uri.TryCreate(id, UriKind.Absolute, out var documentUrl) &&
                 string.Equals(documentUrl.AbsoluteUri, id, StringComparison.Ordinal)
        ? $"Its description is published at <b>{WebUtility.HtmlEncode(documentUrl.Authority)}</b>, and the name is " +
          "that description's claim."
        : "It registered itself with this provider, so the name is its own claim.";

    // The MCP specification: authorization servers "SHOULD display additional warnings for localhost-only redirect
    // URIs". Any program on the user's computer can listen on a loopback port, so the code may reach one the user
    // never meant to authorize.
    var loopbackWarning = request.RedirectUri is { IsLoopback: true }
        ? "<p><b>The code goes to a program on this computer.</b> Any program running on it could receive the code, " +
          "so allow only if you have just started this application yourself.</p>"
        : "";

    // Each scope is granted on its own: the user may allow the application to read without letting it change
    var scopes = string.Join("", request.Scope.Select(scope =>
    {
        var value = WebUtility.HtmlEncode(scope);
        return $"""<li><label><input type="checkbox" name="scope" value="{value}" checked /> {value}</label></li>""";
    }));
    var resources = string.Join("", (request.Resources ?? [])
        .Select(resource => $"<li>{WebUtility.HtmlEncode(resource.OriginalString)}</li>"));
    return $"""
        <!DOCTYPE html>
        <html>
        <head><title>McpServerSample provider</title></head>
        <body>
            <h1>Allow {client}?</h1>
            <p>The application <b>{client}</b> (client ID <code>{clientId}</code>) asks to act for you. {origin}</p>
            <p>If you allow it, the provider sends the authorization code to <b>{redirectHost}</b>: <code>{redirectUri}</code></p>
            {loopbackWarning}
            <form method="post" action="/Auth/Consent">
                <input type="hidden" name="{WebUtility.HtmlEncode(tokens.FormFieldName)}" value="{WebUtility.HtmlEncode(tokens.RequestToken)}" />
                <input type="hidden" name="requestUri" value="{WebUtility.HtmlEncode(requestUri.OriginalString)}" />
                <p>Scopes it may use (clear one to refuse it):</p>
                <ul>{scopes}</ul>
                <p>Resources:</p>
                <ul>{resources}</ul>
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
