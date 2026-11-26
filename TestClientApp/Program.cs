using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Abblix.SecurityEvents.BackChannelLogout;
using Abblix.SecurityEvents.Infrastructure;
using Abblix.SecurityEvents.MinimalApi;
using TestClientApp.BackChannelLogout;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// The Weather page calls ApiSample with the access token from the sign-in
builder.Services.AddHttpClient();

var configuration = builder.Configuration;

const string BackChannelLogoutPathKey = "BackChannelLogoutPath";
const string AuthorityKey = "OpenIdConnect:Authority";
const string ClientIdKey = "OpenIdConnect:ClientId";

// Back-channel logout: the provider calls this address when a session ends there, and the sign-in made through that
// session ends here too. A provider that registers no back-channel logout URI for this client never calls it, and the
// client signs in and out exactly as without it.
var backChannelLogoutPath = configuration[BackChannelLogoutPathKey]
    ?? throw new InvalidOperationException($"Configuration key '{BackChannelLogoutPathKey}' is missing.");
if (!backChannelLogoutPath.StartsWith('/'))
    throw new InvalidOperationException(
        $"Configuration key '{BackChannelLogoutPathKey}' has to be a path such as /signout-backchannel, " +
        $"not '{backChannelLogoutPath}'.");

// The logout token comes from the provider this client signs in with, addressed to this client: the same two values
// the OpenID Connect handler is configured with.
var authority = configuration[AuthorityKey]
    ?? throw new InvalidOperationException($"Configuration key '{AuthorityKey}' is missing.");
var clientId = configuration[ClientIdKey]
    ?? throw new InvalidOperationException($"Configuration key '{ClientIdKey}' is missing.");

// The receiver is Abblix's: it validates the logout token as Back-Channel Logout 1.0 section 2.6 asks (signature,
// issuer, audience, iat, exp, the logout event, no nonce, sub or sid, and a jti used once) and answers the provider
// as section 2.8 asks. The provider's keys are found where its discovery document says. The jti store it registers
// is in-process unless the host registers an IDistributedCache, which several instances need. What the client adds is
// RevokedSessions, the sink that knows where this client's sessions are.
builder.Services.AddSecurityEvents();
builder.Services.AddDiscoveryKeyResolution();
builder.Services.AddBackChannelLogoutReceiver(new BackChannelLogoutValidationOptions
{
    ExpectedIssuers = [authority],
    ExpectedAudience = clientId,
});
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<RevokedSessions>();
builder.Services.AddSingleton<ILogoutNotificationSink>(services => services.GetRequiredService<RevokedSessions>());

builder.Services
    .AddAuthentication(options => configuration.Bind("Authentication", options))
    .AddCookie(options =>
    {
        configuration.Bind(CookieAuthenticationDefaults.AuthenticationScheme, options);

        // A sign-in whose provider session a logout token ended is refused on its next request, which sends the
        // browser back to the provider. The sid comes from the ID token; a provider that issues none leaves the
        // cookie as it is.
        options.Events.OnValidatePrincipal = async context =>
        {
            var sessionId = context.Principal?.FindFirst("sid")?.Value;
            var revokedSessions = context.HttpContext.RequestServices.GetRequiredService<RevokedSessions>();
            if (sessionId is not null && revokedSessions.IsRevoked(sessionId))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(context.Scheme.Name);
            }
        };
    })
    .AddOpenIdConnect(options => configuration.Bind(OpenIdConnectDefaults.AuthenticationScheme, options));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// OpenID Connect Back-Channel Logout 1.0 section 2.5: the provider POSTs the logout token here. No antiforgery check
// applies: the caller is the provider's server, not a browser, and the signed token is what authenticates it.
app.MapBackChannelLogoutEndpoint(backChannelLogoutPath);

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
