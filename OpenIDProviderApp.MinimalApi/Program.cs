using System.Net;
using Abblix.Jwt;
using Abblix.Oidc.Server.Features.RandomGenerators;
using Abblix.Oidc.Server.Features.UserAuthentication;
using Abblix.Oidc.Server.Features.UserInfo;
using Abblix.Oidc.Server.MinimalApi;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using OpenIDProviderApp.MinimalApi;

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

// AddOidcMinimalApi = AddOidcCore + the Minimal API transport adapter
builder.Services.AddOidcMinimalApi(options =>
{
    // Client registrations are loaded from the Oidc section of appsettings.json
    builder.Configuration.Bind("Oidc", options);

    options.Issuer = "https://localhost:5006";
    options.LoginUri = new Uri("/Auth/Login", UriKind.Relative);

    // The following line generates a new key for token signing. Replace it if you want to use your own keys.
    options.SigningKeys = [JsonWebKeyFactory.CreateRsa(PublicKeyUsages.Signature)];
});

builder.Services.AddAuthentication().AddCookie();

// The Minimal API host has no MVC, so the services the pipeline relies on are registered directly
builder.Services.AddAuthorization();
builder.Services.AddCors();
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

app.UseRouting();
app.UseCors();
app.UseAuthorization();

// The Minimal API counterpart of MVC's app.MapControllers()
app.MapOidcEndpoints();

// The login UI: the Minimal API counterpart of the MVC provider's AuthController
app.MapGet("/Auth/Login", (string request_uri) =>
    Results.Content(LoginPage(request_uri, error: null), "text/html"));

app.MapPost("/Auth/Login", async (
        [FromForm] string email,
        [FromForm] string password,
        [FromForm] string requestUri,
        IAuthSessionService authService,
        ISessionIdGenerator sessionIdGenerator,
        TimeProvider timeProvider,
        TestUserStore userStore) =>
    {
        // Attempt to authenticate the user with provided credentials
        if (!userStore.TryAuthenticate(email, password, out var subject))
            return Results.Content(LoginPage(requestUri, "Invalid username or password"), "text/html");

        // If authentication is successful, create a new authentication session
        var authSession = new AuthSession(
            subject,
            sessionIdGenerator.GenerateSessionId(),
            timeProvider.GetUtcNow(),
            CookieAuthenticationDefaults.AuthenticationScheme);

        // Sign in the user using the authentication service
        await authService.SignInAsync(authSession);

        // Redirect the user to the authorization endpoint URL, recovering the OIDC flow
        return Results.Redirect($"/connect/authorize?request_uri={Uri.EscapeDataString(requestUri)}");
    })
    // The form carries a single-use request_uri that binds the post back to the pending
    // authorization request, so the standard antiforgery token adds nothing here
    .DisableAntiforgery();

app.Run();

static string LoginPage(string requestUri, string? error)
{
    var encodedRequestUri = WebUtility.HtmlEncode(requestUri);
    var errorBlock = error == null ? "" : $"<p style=\"color:red\">{WebUtility.HtmlEncode(error)}</p>";
    return $"""
        <!DOCTYPE html>
        <html>
        <head><title>OpenIDProviderApp.MinimalApi</title></head>
        <body>
            <h1>Login</h1>
            {errorBlock}
            <form method="post" action="/Auth/Login">
                <input type="hidden" name="requestUri" value="{encodedRequestUri}" />
                <p><label>Email: <input type="email" name="email" autofocus /></label></p>
                <p><label>Password: <input type="password" name="password" /></label></p>
                <button type="submit">Sign in</button>
            </form>
        </body>
        </html>
        """;
}
