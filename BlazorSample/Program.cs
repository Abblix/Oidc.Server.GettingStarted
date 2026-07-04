using BlazorSample.Components;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Razor components rendered with the interactive Server render mode.
builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

// OIDC client wiring: a local cookie holds the session, OpenID Connect drives the login.
// The same shape as TestClientApp, bound from appsettings so secrets stay out of code.
builder.Services
    .AddAuthentication(options => configuration.Bind("Authentication", options))
    .AddCookie(options => configuration.Bind(CookieAuthenticationDefaults.AuthenticationScheme, options))
    .AddOpenIdConnect(options =>
    {
        configuration.Bind(OpenIdConnectDefaults.AuthenticationScheme, options);

        // MapInboundClaims is false, so claims keep their original short names.
        // Point Identity.Name at the "name" claim instead of the default SOAP URI,
        // otherwise User.Identity.Name comes back empty.
        options.TokenValidationParameters.NameClaimType = "name";
    });

builder.Services.AddAuthorization();

// Makes the signed-in user available to components via AuthorizeView / AuthenticationState.
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Sign-in and sign-out run on the plain HTTP pipeline, never inside an interactive circuit.
// Writing the auth cookie and issuing the OIDC redirect both need the HTTP response, which
// has already started once a Blazor Server component is interactive.
app.MapGet("/auth/login", (string? returnUrl) =>
    Results.Challenge(
        new() { RedirectUri = returnUrl ?? "/" },
        [OpenIdConnectDefaults.AuthenticationScheme]));

// Kept as GET for brevity in this sample. In production trigger sign-out from a POST form
// that carries an antiforgery token, so a stray GET cannot log the user out.
app.MapGet("/auth/logout", () =>
    Results.SignOut(
        new() { RedirectUri = "/" },
        [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
