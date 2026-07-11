using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Abblix.Jwt;
using Abblix.Oidc.Server.Common.Constants;
using Abblix.Oidc.Server.Features.ClientInformation;
using Abblix.Oidc.Server.Features.UserInfo;
using Abblix.Oidc.Server.Mvc;
using AspNetIdentitySample;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ASP.NET Core Identity owns users and credentials: the standard Identity tables
// live in this DbContext, and the core registration brings the user store,
// password hashing, and lockout without Identity's own cookie schemes.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Users")));

builder.Services
    .AddIdentityCore<IdentityUser>(options =>
    {
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

// The library ships no IUserInfoProvider: this registration is mandatory, not optional.
builder.Services.AddScoped<IUserInfoProvider, IdentityUserInfoProvider>();

// Add services to the container.
builder.Services.AddControllersWithViews();

// Register and configure Abblix OIDC Server
builder.Services.AddOidcServices(options =>
{
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
    ];
    options.LoginUri = new Uri("/Auth/Login", UriKind.Relative);
    options.SigningKeys = [JsonWebKeyFactory.CreateRsa(PublicKeyUsages.Signature)];
});

// The host-registered cookie carries the library's OIDC session: Identity never signs in, it only verifies.
// The security stamp snapshotted at login is compared on every request, so a password change
// or UpdateSecurityStampAsync ends existing sessions instead of letting them ride out the cookie lifetime.
builder.Services
    .AddAuthentication()
    .AddCookie(options =>
    {
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Events.OnValidatePrincipal = async context =>
        {
            var userManager = context.HttpContext.RequestServices
                .GetRequiredService<UserManager<IdentityUser>>();
            var subject = context.Principal?.FindFirstValue("sub");
            var user = subject is null ? null : await userManager.FindByIdAsync(subject);
            var snapshot = context.Principal?.FindFirstValue("security_stamp");

            if (user is null || await userManager.GetSecurityStampAsync(user) != snapshot)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(context.Scheme.Name);
            }
        };
    });

builder.Services
    .AddDistributedMemoryCache();

var app = builder.Build();

// Create the Identity schema and seed a first user, so the sample runs out of the box.
// A real deployment replaces this block with EF migrations and its own registration flow.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();

    var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    if (await users.FindByEmailAsync("john.doe@example.com") is null)
    {
        var user = new IdentityUser
        {
            UserName = "john.doe@example.com",
            Email = "john.doe@example.com",
            EmailConfirmed = true,
        };
        var created = await users.CreateAsync(user, "Jd!2024$3cur3");
        if (!created.Succeeded)
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));

        // profile claims Identity does not model live in its claim store:
        // the IdentityUserInfoProvider surfaces them when the profile scope asks
        await users.AddClaimAsync(user, new Claim("name", "John Doe"));
    }
}

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
