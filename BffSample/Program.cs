using System.Net.Http.Headers;
using BffSample.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

var configuration = builder.Configuration;

// The API this BFF forwards to, named twice: to the provider as the resource an access token is
// minted for, and to the forwarder as the address the proxied calls go to.
const string ResourceKey = "OpenIdConnect:Resource";

// RFC 8707 section 2. Microsoft's OpenID Connect handler has no property for it.
const string ResourceParameter = "resource";

var destinationPrefix = configuration.GetValue<string>(ResourceKey)
                        ?? throw new InvalidOperationException($"The value {ResourceKey} must be set");

builder.Services
    .AddAuthorization()
    .AddAuthentication(options => configuration.Bind("Authentication", options))
    // Cookie hardening required of a BFF by draft-ietf-oauth-browser-based-apps section 6.1.3
    // is configured under the "Cookies" section: __Host- name, Secure, HttpOnly, SameSite=Strict.
    .AddCookie(options => configuration.Bind("Cookies", options))
    .AddOpenIdConnect(options =>
    {
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
    });

builder.Services.AddControllers();
builder.Services.AddHttpForwarder();

builder.Services.AddCors(
    options => options.AddPolicy(
        BffController.CorsPolicyName,
        policyBuilder =>
        {
            var allowedOrigins = configuration.GetSection("CorsSettings:AllowedOrigins").Get<string[]>();

            if (allowedOrigins is { Length: > 0 })
                policyBuilder.WithOrigins(allowedOrigins);

            policyBuilder
                .WithMethods(HttpMethods.Get)
                .AllowCredentials();
        }));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseStaticFiles();
app.UseCors(BffController.CorsPolicyName);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();


app.MapForwarder(
    "/bff/{**catch-all}",
    destinationPrefix,
    builderContext =>
    {
        // Cut the "/bff" prefix from the request path
        builderContext.AddPathRemovePrefix("/bff");
        builderContext.AddRequestTransform(async transformContext =>
        {
            // Strip the session cookie before forwarding: the resource server authenticates on the
            // bearer token alone and must never see the BFF's session (draft section 6.1.1, step K).
            transformContext.ProxyRequest.Headers.Remove("Cookie");

            // Attach the access token kept in the session, so it never reaches the browser.
            var accessToken = await transformContext.HttpContext.GetTokenAsync(OpenIdConnectParameterNames.AccessToken);
            transformContext.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        });
    }).RequireAuthorization();

app.MapFallbackToFile("index.html").RequireAuthorization();

app.Run();
