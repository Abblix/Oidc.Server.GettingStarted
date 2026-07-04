using System.Security.Cryptography;
using System.Text;
using Abblix.DependencyInjection;
using Abblix.Jwt;
using Abblix.Oidc.Server.Common.Constants;
using Abblix.Oidc.Server.Features.ClientInformation;
using Abblix.Oidc.Server.Features.UserInfo;
using Abblix.Oidc.Server.Mvc;
using OpenIDProviderApp;

var builder = WebApplication.CreateBuilder(args);

// Add the TestUserStorage as a singleton service in the DI container.
var userInfoStorage = new TestUserStorage(
    new UserInfo(
        Subject: "1234567890",
        Name: "John Doe",
        Email: "john.doe@example.com",
        Password: "Jd!2024$3cur3")
);
builder.Services.AddSingleton(userInfoStorage);

// Use AddAlias to register TestUserStorage also as an implementation of IUserInfoProvider.
builder.Services.AddAlias<IUserInfoProvider, TestUserStorage>();

// Add services to the container.
builder.Services.AddControllersWithViews();

// Register and configure Abblix OIDC Server
builder.Services.AddOidcServices(options =>
{
    // options.Scopes = [new ScopeDefinition(...)];
    options.Resources =
    [
        new ResourceDefinition(new Uri("https://localhost:5004", UriKind.Absolute), new ScopeDefinition("weather")),
    ];
    options.Clients =
    [
        new ClientInfo("test_client") {
            ClientSecrets = [new ClientSecret { Sha512Hash = SHA512.HashData(Encoding.UTF8.GetBytes("secret")) }],
            TokenEndpointAuthMethod = ClientAuthenticationMethods.ClientSecretPost,
            AllowedGrantTypes = [GrantTypes.AuthorizationCode],
            PkceRequired = true,
            RedirectUris = [new Uri("https://localhost:5002/signin-oidc", UriKind.Absolute)],
            PostLogoutRedirectUris = [new Uri("https://localhost:5002/signout-callback-oidc", UriKind.Absolute)],
        },
        new ClientInfo("bff_sample") {
            ClientSecrets = [new ClientSecret { Sha512Hash = SHA512.HashData(Encoding.UTF8.GetBytes("secret")) }],
            TokenEndpointAuthMethod = ClientAuthenticationMethods.ClientSecretPost,
            AllowedGrantTypes = [GrantTypes.AuthorizationCode],
            PkceRequired = true,
            RedirectUris = [new Uri("https://localhost:5003/signin-oidc", UriKind.Absolute)],
            PostLogoutRedirectUris = [new Uri("https://localhost:5003/signout-callback-oidc", UriKind.Absolute)],
        },
        new ClientInfo("blazor_sample") {
            ClientSecrets = [new ClientSecret { Sha512Hash = SHA512.HashData(Encoding.UTF8.GetBytes("secret")) }],
            TokenEndpointAuthMethod = ClientAuthenticationMethods.ClientSecretPost,
            AllowedGrantTypes = [GrantTypes.AuthorizationCode],
            PkceRequired = true,
            RedirectUris = [new Uri("https://localhost:5005/signin-oidc", UriKind.Absolute)],
            PostLogoutRedirectUris = [new Uri("https://localhost:5005/signout-callback-oidc", UriKind.Absolute)],
        }
    ];
    options.LoginUri = new Uri("/Auth/Login", UriKind.Relative);
    options.SigningKeys = [JsonWebKeyFactory.CreateRsa(PublicKeyUsages.Signature)];
});

// Add authentication services
builder.Services
    .AddAuthentication()
    .AddCookie();

builder.Services
    .AddDistributedMemoryCache();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseCors();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
