using Abblix.DependencyInjection;
using Abblix.Jwt;
using Abblix.Oidc.Server.Common.Constants;
using Abblix.Oidc.Server.Features.ClientInformation;
using Abblix.Oidc.Server.Features.Consents;
using Abblix.Oidc.Server.Features.MultiTenancy;
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

// Grants every client everything it asks for, which suits only clients of your own;
// to ask the end user for consent, register your own IUserConsentsProvider instead
builder.Services.AddConsentGrantedForEveryClient();

// Register and configure Abblix OIDC Server, serving one issuer
builder.Services.AddOidcServices(options => options.Tenants.Add(new TenantDefinition
{
    Id = "main",
    // The address the app is served on, written into every token it issues
    Issuer = new Uri("https://localhost:5001"),

    // Client registrations are loaded from the Oidc section of appsettings.json. Only the clients are read from
    // there: the tenant itself is declared here, because its signing key is generated at startup, so binding
    // Oidc:Tenants from configuration as well would add a second tenant rather than fill in this one.
    Clients = builder.Configuration.GetSection("Oidc:Clients").Get<ClientInfo[]>() ?? [],

    Resources =
    [
        new ResourceDefinition(new Uri("https://localhost:5004", UriKind.Absolute), new ScopeDefinition("weather")),
    ],
    LoginUri = new Uri("/Auth/Login", UriKind.Relative),
    SigningKeys = [JsonWebKeyFactory.CreateRsa(PublicKeyUsages.Signature)],
}));

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

// The library's controllers carry no routes of their own, so the default route below never reaches them
app.MapOidcControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
