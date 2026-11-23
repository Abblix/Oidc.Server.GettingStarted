using System.Net.Http.Headers;
using BffSample;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

var configuration = builder.Configuration;

// The API this BFF forwards to, named twice: to the provider as the resource an access token is
// minted for, and to the forwarder as the address the proxied calls go to.
const string ResourceKey = "OpenIdConnect:Resource";

// RFC 8707 section 2. Microsoft's OpenID Connect handler has no property for it.
const string ResourceParameter = "resource";

const string ContentSecurityPolicyKey = "ContentSecurityPolicy";
const string LogoutTicketLifetimeKey = "LogoutTicketLifetime";

// Everything the SPA calls on the BFF lies under this path, and the SPA's own pages outside it
const string BffPath = "/bff";

var destinationPrefix = configuration.GetValue<string>(ResourceKey)
                        ?? throw new InvalidOperationException($"The value {ResourceKey} must be set");

builder.Services
    .AddAuthorization()
    .AddAuthentication(options => configuration.Bind("Authentication", options))
    // The session cookie follows RFC 10017 section 6.1.3.2, configured under the "Cookies" section: Secure and
    // HttpOnly (MUST), SameSite=Strict and the __Host-Http- name prefix (SHOULD).
    .AddCookie(options => configuration.Bind("Cookies", options))
    .AddOpenIdConnect(options =>
    {
        // The handler's correlation and nonce cookies keep the handler's defaults, which depart from RFC 10017
        // section 6.1.3.2, and the section asks a departure to state its reason. They are SameSite=None because
        // the provider returns the code in a form posted from its own site (response_mode=form_post), and a
        // cross-site POST carries no Lax or Strict cookie. They live on the callback path rather than on /, so
        // they cannot take the __Host- prefix. They are still Secure and HttpOnly, carry no token, and last only
        // for one sign-in.
        configuration.Bind("OpenIdConnect", options);

        // The weather scope belongs to a protected resource, and RFC 8707 section 2 names that
        // resource with the "resource" request parameter. The provider resolves a scope against
        // the resources the request names, so without this the authorization request comes back
        // refused with invalid_scope. Microsoft's handler exposes no property for the parameter,
        // so it is set on the outgoing message.
        options.Events.OnRedirectToIdentityProvider = context =>
        {
            context.ProtocolMessage.SetParameter(ResourceParameter, destinationPrefix);
            return Task.CompletedTask;
        };

        // Logout is a POST carrying the CSRF header, so the SPA sends it with fetch, and a fetch cannot take the
        // browser to the provider's end-session endpoint. The address, with the state the handler would have put
        // there, waits on the server under a ticket, because it carries the ID token as id_token_hint; the SPA gets
        // an address of the BFF's own to navigate to, which redirects to the provider (see BffController).
        options.Events.OnRedirectToIdentityProviderForSignOut = async context =>
        {
            context.ProtocolMessage.State = context.Options.StateDataFormat.Protect(context.Properties);
            var tickets = context.HttpContext.RequestServices.GetRequiredService<LogoutTickets>();
            var ticket = tickets.Issue(context.ProtocolMessage.CreateLogoutRequestUrl());
            await context.Response.WriteAsJsonAsync(new { next = $"{BffPath}/logout/continue?ticket={ticket}" });
            context.HandleResponse();
        };
    });

builder.Services.AddControllers();
builder.Services.AddHttpForwarder();
builder.Services.AddMemoryCache();

var logoutTicketLifetime = configuration.GetValue<TimeSpan?>(LogoutTicketLifetimeKey)
                           ?? throw new InvalidOperationException($"The value {LogoutTicketLifetimeKey} must be set");
if (logoutTicketLifetime <= TimeSpan.Zero)
    throw new InvalidOperationException($"The value {LogoutTicketLifetimeKey} must be a positive time span");
builder.Services.AddSingleton(services => new LogoutTickets(
    services.GetRequiredService<IMemoryCache>(), logoutTicketLifetime));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Script injected into the SPA reaches everything the BFF does on the user's behalf, and no BFF can stop it once it
// runs (RFC 10017 section 6.1.4.1), so the published SPA may load script, style and data only from its own origin.
// The Vite build emits no inline script for the policy to block. In development Vite serves the SPA, not the BFF.
var contentSecurityPolicy = configuration.GetValue<string>(ContentSecurityPolicyKey)
                            ?? throw new InvalidOperationException($"The value {ContentSecurityPolicyKey} must be set");
app.Use((context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy = contentSecurityPolicy;
    return next(context);
});

app.UseHttpsRedirection();
app.UseRouting();

// Every request under /bff carries the SPA's static CSRF header (RFC 10017 section 6.1.3.3.2): a page on another
// origin cannot add it without a CORS preflight, and the BFF approves none. SameSite=Strict alone does not stop a
// sibling application on the same site, as section 6.1.3.3.1 says, and every sample here shares the site localhost.
// Only the endpoints the browser navigates to are exempt, because a navigation cannot carry a header.
app.Use((context, next) =>
{
    if (!context.Request.Path.StartsWithSegments(BffPath) ||
        context.GetEndpoint()?.Metadata.GetMetadata<BrowserNavigationAttribute>() != null ||
        CsrfHeader.IsPresent(context.Request))
    {
        return next(context);
    }

    context.Response.StatusCode = StatusCodes.Status400BadRequest;
    return Task.CompletedTask;
});

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Each address the SPA may reach through the BFF is named, with its method, and goes to one address of the API
// (RFC 10017 section 6.1.3.6): a path forwarded as it comes would carry the user's access token to whatever else
// the API serves.
app.MapForwarder(
    "/bff/weatherforecast",
    destinationPrefix,
    builderContext =>
    {
        // Cut the "/bff" prefix from the request path
        builderContext.AddPathRemovePrefix(BffPath);
        builderContext.AddRequestTransform(async transformContext =>
        {
            // Strip the session cookie before forwarding: the resource server authenticates on the bearer token
            // alone and must never see the BFF's session (RFC 10017 section 6.1.1, step K).
            transformContext.ProxyRequest.Headers.Remove("Cookie");

            // Attach the access token kept in the session, so it never reaches the browser.
            var accessToken = await transformContext.HttpContext.GetTokenAsync(OpenIdConnectParameterNames.AccessToken);
            transformContext.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        });
    })
    .WithMetadata(new HttpMethodMetadata([HttpMethods.Get]))
    .RequireAuthorization();

// Any other address under /bff is not found, rather than answered with the SPA's page by the fallback below
app.Map($"{BffPath}/{{**rest}}", () => Results.NotFound());

app.MapFallbackToFile("index.html").RequireAuthorization();

app.Run();
