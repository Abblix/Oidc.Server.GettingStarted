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
    .AddCookie()
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
            // Get the access token received earlier during the authentication process
            var accessToken = await transformContext.HttpContext.GetTokenAsync(OpenIdConnectParameterNames.AccessToken);

            // Append a header with access token to the proxy request
            transformContext.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        });
    }).RequireAuthorization();

app.MapFallbackToFile("index.html").RequireAuthorization();

app.Run();
