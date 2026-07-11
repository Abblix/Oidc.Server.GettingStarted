using System.Net.Http.Headers;
using BffSample.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

var configuration = builder.Configuration;

builder.Services
    .AddAuthorization()
    .AddAuthentication(options => configuration.Bind("Authentication", options))
    // Cookie hardening required of a BFF by draft-ietf-oauth-browser-based-apps section 6.1.3
    // is configured under the "Cookies" section: __Host- name, Secure, HttpOnly, SameSite=Strict.
    .AddCookie(options => configuration.Bind("Cookies", options))
    .AddOpenIdConnect(options => configuration.Bind("OpenIdConnect", options));

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

app.MapControllerRoute(
    name: "default",
    pattern: "{controller}/{action=Index}/{id?}");

const string key = "OpenIdConnect:Resource";
var destinationPrefix = configuration.GetValue<string>(key)
                        ?? throw new InvalidOperationException($"The value {key} must be set");

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
