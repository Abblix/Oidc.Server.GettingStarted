using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Abblix.DependencyInjection;
using Abblix.Jwt;
using Abblix.Oidc.Server.Common.Constants;
using Abblix.Oidc.Server.Common.Interfaces;
using Abblix.Oidc.Server.Features.ClientInformation;
using Abblix.Oidc.Server.Features.UserInfo;
using Abblix.Oidc.Server.Mvc;
using AspNetIdentitySample;
using AspNetIdentitySample.OidcStore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
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

// Password-hashing work factor: Identity's default hasher here is PBKDF2-HMAC-SHA512. Bind
// IterationCount from appsettings.json so it can be raised to meet current OWASP guidance for that
// algorithm without a recompile.
builder.Services.Configure<PasswordHasherOptions>(builder.Configuration.GetSection("PasswordHasher"));

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
    options.LoginUri = new Uri("/Auth/Login", UriKind.Relative);

    // SigningKeys and Clients are deliberately NOT set here. They come from the SQLite-backed
    // providers registered below, so they outlive a restart. A first-party client could still be
    // declared in options.Clients and would win on read (see LayeredClientInfoProvider).
});

// Durable OIDC state (signing keys and client registrations) lives in SQLite so a restart neither
// invalidates issued tokens nor forgets registered clients. The providers below are singletons, so
// they open a context per call through IDbContextFactory rather than holding a scoped DbContext.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContextFactory<OidcStoreDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("OidcStore")));

// Data Protection encrypts the signing key before it lands in SQLite, so a stolen database file
// yields ciphertext, not a private key. The key ring is persisted to disk EXPLICITLY rather than
// through the default, which lands in a per-user profile folder (why a local restart still decrypts)
// but degrades to ephemeral in-memory keys inside a container with no user profile. SetApplicationName
// fixes the key isolation so every instance derives the same keys.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(
        System.IO.Path.Combine(builder.Environment.ContentRootPath, "dataprotection-keys")))
    .SetApplicationName("AspNetIdentitySample");
#warning The Data Protection key ring above is a LOCAL, UNENCRYPTED folder: single-node and unprotected. For real deployments persist it to a store every instance shares and add ProtectKeysWith an X.509 certificate or a KMS, or a fresh pod cannot decrypt the stored signing key. See https://docs.abblix.com/docs/signing-key-persistence#encrypting-the-stored-key-at-rest

// Signing keys: replace the ephemeral in-memory default (OidcOptionsKeysProvider, which would
// generate a new key on every startup) with the database-backed provider. Registered after
// AddOidcServices so the last singular registration wins.
builder.Services.AddSingleton<IAuthServiceKeysProvider, DatabaseKeysProvider>();

// Client store: decorate the read seam and replace the write seam, after AddOidcServices. The
// library aliases its in-memory store to both client interfaces unconditionally, so a store
// pre-registered before AddOidcServices would be silently overridden. Decorating and replacing
// afterwards is the pattern that takes effect on this version.
builder.Services.AddSingleton<DurableClientStore>();
builder.Services.Decorate<IClientInfoProvider, LayeredClientInfoProvider>();
builder.Services.RemoveAll<IClientInfoManager>();
builder.Services.AddAlias<IClientInfoManager, DurableClientStore>();

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
        // Generate the seed password instead of hardcoding one: nothing sensitive lands in source,
        // and Identity stores only its salted PBKDF2 hash, never the plaintext.
        var password = GeneratePassword();
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));

        // profile claims Identity does not model live in its claim store:
        // the IdentityUserInfoProvider surfaces them when the profile scope asks
        await users.AddClaimAsync(user, new Claim("name", "John Doe"));

#warning DEV convenience only: even on the console this credential reaches container stdout and any log sink scraping it. A real deployment never surfaces a password: seed with no usable password and email a reset link, or force a change on first login.
        // Show the generated password once, on the console, so the sample is runnable out of the box.
        Console.WriteLine($"Seeded john.doe@example.com with a one-time generated password: {password}");

        // Pause so an operator at a terminal can copy the password before startup logs scroll it away.
        // Skipped when stdin is not interactive (container, CI, a background run) so it never hangs a headless start.
        if (!Console.IsInputRedirected)
        {
            Console.WriteLine("Copy it now, then press Enter to start the server...");
            Console.ReadLine();
        }
    }

    // Seed the OIDC store so the sample runs out of the box: a signing key that survives restarts,
    // and the test client. On the next run both already exist, so the same key keeps validating
    // tokens issued before the restart. A real deployment replaces EnsureCreated with EF migrations,
    // and keeps signing keys in an HSM or KMS rather than the database (see the #warning on PersistedSigningKey).
    var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

    var oidcStoreFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OidcStoreDbContext>>();
    await using (var oidcStore = await oidcStoreFactory.CreateDbContextAsync())
    {
        await oidcStore.Database.EnsureCreatedAsync();

        if (!await oidcStore.SigningKeys.AnyAsync(key => key.IsActive && key.Usage == PublicKeyUsages.Signature))
        {
            var keyProtector = scope.ServiceProvider
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(DatabaseKeysProvider.SigningKeyProtectorPurpose);

            var signingKey = JsonWebKeyFactory.CreateRsa(PublicKeyUsages.Signature);
            oidcStore.SigningKeys.Add(new PersistedSigningKey
            {
                KeyId = signingKey.KeyId!,
                Usage = PublicKeyUsages.Signature,
                JwkJson = keyProtector.Protect(JsonSerializer.Serialize<JsonWebKey>(signingKey)),
                CreatedAt = clock.GetUtcNow(),
            });
            await oidcStore.SaveChangesAsync();
        }
    }

    var clients = scope.ServiceProvider.GetRequiredService<DurableClientStore>();
    if (await clients.TryFindClientAsync("test_client") is null)
    {
        await clients.AddClientAsync(new ClientInfo("test_client")
        {
            ClientSecrets = [new ClientSecret { Sha512Hash = SHA512.HashData(Encoding.UTF8.GetBytes("secret")) }],
            TokenEndpointAuthMethod = ClientAuthenticationMethods.ClientSecretPost,
            AllowedGrantTypes = [GrantTypes.AuthorizationCode],
            PkceRequired = true,
            RedirectUris = [new Uri("https://localhost:5002/signin-oidc", UriKind.Absolute)],
            PostLogoutRedirectUris = [new Uri("https://localhost:5002/signout-callback-oidc", UriKind.Absolute)],
        });
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

// Builds a cryptographically random password that satisfies the default Identity policy: one
// character guaranteed from each required class (upper, lower, digit, non-alphanumeric), the rest
// drawn from the full set, then shuffled so the guaranteed characters are not always in front.
static string GeneratePassword()
{
    const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    const string lower = "abcdefghijkmnpqrstuvwxyz";
    const string digits = "23456789";
    const string special = "!@#$%^&*-_";
    const string all = upper + lower + digits + special;

    var chars = new char[16];
    chars[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
    chars[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
    chars[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
    chars[3] = special[RandomNumberGenerator.GetInt32(special.Length)];
    for (var i = 4; i < chars.Length; i++)
        chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];

    for (var i = chars.Length - 1; i > 0; i--)
    {
        var j = RandomNumberGenerator.GetInt32(i + 1);
        (chars[i], chars[j]) = (chars[j], chars[i]);
    }

    return new string(chars);
}
