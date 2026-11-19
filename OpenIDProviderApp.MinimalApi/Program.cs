using System.Net;
using Abblix.Jwt;
using Abblix.Oidc.Server.Common.Configuration;
using Abblix.Oidc.Server.Common.Constants;
using Abblix.Oidc.Server.Common.Interfaces;
using Abblix.Oidc.Server.Features;
using Abblix.Oidc.Server.Features.ClientInformation;
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
using OpenIDProviderApp.MinimalApi;
using UriBuilder = Abblix.Utils.UriBuilder;

var builder = WebApplication.CreateBuilder(args);

// Host-provided contract: the library resolves user claims through this interface
var userStore = new TestUserStore(
    new TestUser(
        Subject: "1234567890",
        Name: "John Doe",
        Email: "john.doe@example.com",
        Password: "Jd!2024$3cur3"));
builder.Services.AddSingleton(userStore);
builder.Services.AddSingleton<IUserInfoProvider>(userStore);

// Grants every client everything it asks for, which suits only clients of your own;
// to ask the end user for consent, register your own IUserConsentsProvider instead
builder.Services.AddConsentGrantedForEveryClient();

// AddOidcServices = the protocol core plus the Minimal API transport adapter, the counterpart
// of the MVC integration's own AddOidcServices. It serves one issuer, declared as one tenant.
builder.Services.AddOidcServices(options => options.Tenants.Add(new TenantDefinition
{
    Id = "main",
    // The address the app is served on, written into every token it issues
    Issuer = new Uri("https://localhost:5006"),

    // Client registrations are loaded from the Oidc section of appsettings.json. Only the clients are read from
    // there: the tenant itself is declared here, because its signing key is generated at startup, so binding
    // Oidc:Tenants from configuration as well would add a second tenant rather than fill in this one.
    Clients = builder.Configuration.GetSection("Oidc:Clients").Get<ClientInfo[]>() ?? [],

    LoginUri = new Uri("/Auth/Login", UriKind.Relative),

    // The same resource the MVC provider declares, so a client registered against both - BffSample
    // asks for the weather scope - is served identically whichever one it points at.
    Resources =
    [
        new ResourceDefinition(new Uri("https://localhost:5004", UriKind.Absolute), new ScopeDefinition("weather")),
    ],

    // The following line generates a new key for token signing. Replace it if you want to use your own keys.
    SigningKeys = [JsonWebKeyFactory.CreateRsa(PublicKeyUsages.Signature)],
}));

// Token introspection (RFC 7662) is opt-in: this call registers the endpoint and advertises it in discovery.
// A client authenticates to it with its own credentials, as requests.http does with console_client.
builder.Services.AddIntrospection();

builder.Services.AddAuthentication().AddCookie();

// The Minimal API host has no MVC, so the services the pipeline relies on are registered directly
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery();

var app = builder.Build();

app.UseRouting();
app.UseCors();
app.UseAuthorization();

// The Minimal API counterpart of the MVC provider's app.MapOidcControllers()
app.MapOidcEndpoints();

// The login UI: the Minimal API counterpart of the MVC provider's AuthController
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
        // would be signed in as the attacker, and whatever the victim then authorizes or enters lands in the
        // attacker's account. The antiforgery token the form carries is what such a page cannot supply. A form
        // opened in another tab before signing in carries a token that no longer matches, so it is shown again.
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            var fresh = antiforgery.GetAndStoreTokens(context);
            return Results.Content(LoginPage(fresh, requestUri, "The sign-in form has expired, please sign in again"),
                "text/html");
        }

        // Attempt to authenticate the user with provided credentials
        if (!userStore.TryAuthenticate(email, password, out var subject))
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Content(LoginPage(tokens, requestUri, "Invalid username or password"), "text/html");
        }

        // If authentication is successful, create a new authentication session
        var authSession = new AuthSession(
            subject,
            sessionIdGenerator.GenerateSessionId(),
            timeProvider.GetUtcNow(),
            CookieAuthenticationDefaults.AuthenticationScheme);

        // Sign in the user using the authentication service
        await authService.SignInAsync(authSession);

        // A visit to the login page on its own carries no authorization request to resume, and this host has no
        // page of its own to return to
        if (string.IsNullOrEmpty(requestUri))
            return Results.Content(SignedInPage(), "text/html");

        // Redirect the user to the authorization endpoint URL, recovering the OIDC flow. The resolver answers the
        // address as mapped, a route prefix or a path base included; every endpoint is enabled here, as the
        // defaults leave them, so it always has an address to return.
        var authorizeUrl = new UriBuilder(endpointResolver.Resolve(OidcEndpoints.Authorize)!)
            { Query = { [AuthorizationRequest.Parameters.RequestUri] = requestUri } };
        return Results.Redirect(authorizeUrl);
    })
    // The handler checks the antiforgery token itself, so an expired form gets the login page again
    // rather than the bare 400 the automatic check answers
    .DisableAntiforgery();

app.Run();

static string LoginPage(AntiforgeryTokenSet tokens, string? requestUri, string? error)
{
    var encodedRequestUri = WebUtility.HtmlEncode(requestUri ?? "");
    var errorBlock = error == null ? "" : $"<p style=\"color:red\">{WebUtility.HtmlEncode(error)}</p>";
    return $"""
        <!DOCTYPE html>
        <html>
        <head><title>OpenIDProviderApp.MinimalApi</title></head>
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
    <head><title>OpenIDProviderApp.MinimalApi</title></head>
    <body>
        <h1>Signed in</h1>
        <p>Open a client application to continue: it sends you here when it needs a sign-in.</p>
    </body>
    </html>
    """;
