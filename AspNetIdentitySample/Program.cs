using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Abblix.DependencyInjection;
using Abblix.Jwt;
using Abblix.Oidc.Server.Common.Constants;
using Abblix.Oidc.Server.Common.Interfaces;
using Abblix.Oidc.Server.Features.ClientInformation;
using Abblix.Oidc.Server.Features.RandomGenerators;
using Abblix.Oidc.Server.Features.UserAuthentication;
using Abblix.Oidc.Server.Features.UserInfo;
using Abblix.Oidc.Server.MinimalApi;
using Abblix.Oidc.Server.Model;
using AspNetIdentitySample;
using AspNetIdentitySample.Models;
using AspNetIdentitySample.OidcStore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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

// A Minimal API host has no MVC, so the services the request pipeline and the auth UI rely on are
// registered directly (the MVC host got these transitively from AddControllersWithViews).
builder.Services.AddAuthorization();
builder.Services.AddCors();
builder.Services.AddMemoryCache();

// The React auth SPA posts JSON and echoes the antiforgery request token in this header; the auth
// endpoints validate it against the paired HttpOnly cookie.
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

// Emit an OpenAPI document from the auth endpoints. It is the contract the React client generates its
// TypeScript types from, so the two sides cannot silently drift. Served at /openapi/v1.json in
// development; the client regenerates its types from it with `npm run gen:api` (see ClientApp).
builder.Services.AddOpenApi(options =>
    // Scope the document to the auth API. The client only generates types for its own contract, so the
    // library's OIDC protocol endpoints stay out of the generated schema and it stays small.
    options.ShouldInclude = description => description.RelativePath?.StartsWith("api/auth") ?? false);

// Register and configure Abblix OIDC Server through the Minimal API adapter: the same framework-neutral
// core the MVC integration registers, with a different transport. The endpoints are mapped later with
// app.MapOidcEndpoints().
builder.Services.AddOidcServices(options =>
{
    options.Issuer = "https://localhost:5001";
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

// Client store: decorate the read seam and replace the write seam, after AddOidcServices.
// The order is what Decorate needs - it wraps the registration already in the collection, so
// the library's own must be there first. Registering a store BEFORE would also work, since
// the library aliases its in-memory one with TryAddAlias and a host registration wins - but
// then the decorator would wrap this store rather than the library's, and the clients from
// configuration would quietly disappear instead of being the layer underneath.
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

// Create the Identity schema so the sign-up form has tables to write to. Nothing is seeded here:
// users register themselves through /Auth/Register (the endpoints below), so the very first thing a
// fresh run asks for is a sign-up. A real deployment replaces EnsureCreated with EF migrations.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();

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

    // The same clients OpenIDProviderApp registers, so this sample is a drop-in replacement for it:
    // any client in the solution reaches it on the same port with no change of its own. Here they
    // are rows in SQLite that survive a restart and are edited through the registration endpoint,
    // there they are configuration read at startup.
    var clients = scope.ServiceProvider.GetRequiredService<DurableClientStore>();
    foreach (var (clientId, port, claimsInIdentityToken) in new[]
             {
                 ("test_client", 5002, false),

                 // BffSample asks for a token addressed to the API, which the userinfo endpoint
                 // then refuses, so its profile claims have to travel in the ID token.
                 ("bff_sample", 5003, true),

                 ("blazor_sample", 5005, false),
             })
    {
        if (await clients.TryFindClientAsync(clientId) is not null)
            continue;

        await clients.AddClientAsync(new ClientInfo(clientId)
        {
            ClientSecrets = [new ClientSecret { Sha512Hash = SHA512.HashData(Encoding.UTF8.GetBytes("secret")) }],
            TokenEndpointAuthMethod = ClientAuthenticationMethods.ClientSecretPost,
            AllowedGrantTypes = [GrantTypes.AuthorizationCode],
            PkceRequired = true,
            RedirectUris = [new Uri($"https://localhost:{port}/signin-oidc", UriKind.Absolute)],
            PostLogoutRedirectUris = [new Uri($"https://localhost:{port}/signout-callback-oidc", UriKind.Absolute)],
            ForceUserClaimsInIdentityToken = claimsInIdentityToken,
        });
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseCors();
app.UseAuthorization();
app.UseAntiforgery();

// The OIDC protocol endpoints (discovery, authorize, token, userinfo, end-session, ...): the Minimal
// API counterpart of the MVC host's app.MapControllers().
app.MapOidcEndpoints();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// The auth UI. The OIDC library redirects an unauthenticated user to LoginUri (/Auth/Login); both auth
// routes return the same React SPA, which reads request_uri from the query and posts to the JSON API
// below. Serving the page issues the antiforgery request token in a JS-readable cookie the SPA echoes.
app.MapGet("/Auth/Login", ServeAuthSpa);
app.MapGet("/Auth/Register", ServeAuthSpa);

// The JSON contract the SPA talks to. The group filter validates the antiforgery header on every POST.
var authApi = app.MapGroup("/api/auth");
authApi.AddEndpointFilter(ValidateAntiforgeryAsync);
authApi.MapPost("/login", LoginAsync)
    .Produces<AuthSuccessResponse>()
    .ProducesValidationProblem();
authApi.MapPost("/register", RegisterAsync)
    .Produces<AuthSuccessResponse>()
    .ProducesValidationProblem();

app.Run();

// Serves the built React SPA (wwwroot/auth) and issues the antiforgery request token in a JS-readable
// cookie. The SPA echoes it in the X-CSRF-TOKEN header on every POST; a server-rendered form did this
// with a hidden field, but a SPA has to perform the handshake explicitly.
static async Task<IResult> ServeAuthSpa(IAntiforgery antiforgery, HttpContext http, IWebHostEnvironment environment)
{
    var tokens = antiforgery.GetAndStoreTokens(http);
    http.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
    {
        HttpOnly = false,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
    });

    var indexHtml = System.IO.Path.Combine(environment.WebRootPath, "auth", "index.html");
    return Results.Content(await File.ReadAllTextAsync(indexHtml), "text/html");
}

// A JSON POST to a Minimal API endpoint gets no automatic antiforgery, so validate the header token
// (against the paired HttpOnly cookie) here and shape a failure as a 400, not the default 500.
static async ValueTask<object?> ValidateAntiforgeryAsync(
    EndpointFilterInvocationContext context, EndpointFilterDelegate next)
{
    var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
    try
    {
        await antiforgery.ValidateRequestAsync(context.HttpContext);
    }
    catch (AntiforgeryValidationException)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [string.Empty] = ["Antiforgery validation failed. Reload the page and try again."],
        });
    }

    return await next(context);
}

// POST /api/auth/login. Identity verifies the password (hash, lockout) but issues no cookie; on success
// the library session is established and the authorize URL is returned for the SPA to follow.
static async Task<IResult> LoginAsync(
    LoginRequest request,
    IAuthSessionService authService,
    ISessionIdGenerator sessionIdGenerator,
    UserManager<IdentityUser> userManager,
    SignInManager<IdentityUser> signIn,
    TimeProvider clock,
    IOptions<OidcRouteOptions> routeOptions)
{
    var user = await userManager.FindByEmailAsync(request.Email);

    var result = user is not null
        ? await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)
        : SignInResult.Failed;

    if (!result.Succeeded)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["password"] = ["Invalid email or password."],
        });

    var redirectUrl = await SignInAndBuildResumeUrl(
        authService, sessionIdGenerator, userManager, clock, routeOptions.Value, user!, request.RequestUri);
    return Results.Ok(new AuthSuccessResponse { RedirectUrl = redirectUrl });
}

// POST /api/auth/register. Creates the account through UserManager (salted PBKDF2 hash, unique-email and
// password policy enforced), writes the profile name into Identity's claim store, then signs in and
// resumes the OIDC flow. There is no seeded user; this is how the first account is created.
static async Task<IResult> RegisterAsync(
    RegisterRequest request,
    IAuthSessionService authService,
    ISessionIdGenerator sessionIdGenerator,
    UserManager<IdentityUser> userManager,
    TimeProvider clock,
    IOptions<OidcRouteOptions> routeOptions)
{
    // Minimal API does not auto-run the model's data annotations, so check the one that UserManager
    // cannot: that the two password boxes match.
    if (request.Password != request.ConfirmPassword)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["confirmPassword"] = ["The password and its confirmation do not match."],
        });

#warning DEV shortcut: sign-up leaves the address unconfirmed, so email_verified is honestly false, yet still lets the account sign in. A real deployment emails a confirmation link and only trusts the address once the user proves control of the mailbox.
    var user = new IdentityUser
    {
        UserName = request.Email,
        Email = request.Email,
        EmailConfirmed = false,
    };

    var created = await userManager.CreateAsync(user, request.Password);
    if (!created.Succeeded)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            // Surface Identity's own messages (password too short, email already taken, ...) inline.
            ["password"] = created.Errors.Select(error => error.Description).ToArray(),
        });

    // profile claims Identity does not model live in its claim store; the IdentityUserInfoProvider
    // surfaces them when the profile scope asks.
    await userManager.AddClaimAsync(user, new Claim("name", request.Name));

    var redirectUrl = await SignInAndBuildResumeUrl(
        authService, sessionIdGenerator, userManager, clock, routeOptions.Value, user, request.RequestUri);
    return Results.Ok(new AuthSuccessResponse { RedirectUrl = redirectUrl });
}

// Login and sign-up share their tail: establish the library session, then return the authorize URL that
// resumes the OIDC flow the user was pulled out of. The Minimal API adapter has no MVC route-token
// table, so the route comes from OidcRouteOptions (its Authorize defaults to /connect/authorize).
static async Task<string> SignInAndBuildResumeUrl(
    IAuthSessionService authService,
    ISessionIdGenerator sessionIdGenerator,
    UserManager<IdentityUser> userManager,
    TimeProvider clock,
    OidcRouteOptions routes,
    IdentityUser user,
    string? requestUri)
{
    var authSession = new AuthSession(
        user.Id,                                             // the subject the claims adapter resolves later
        sessionIdGenerator.GenerateSessionId(),
        clock.GetUtcNow(),
        CookieAuthenticationDefaults.AuthenticationScheme)
    {
        Email = user.Email,
        EmailVerified = user.EmailConfirmed,
        AuthenticationMethodReferences = ["pwd"],            // lands in the amr claim

        // snapshot the security stamp: the cookie's OnValidatePrincipal compares it on every
        // request, so a password change or UpdateSecurityStampAsync ends this session
        AdditionalClaims = new JsonObject
        {
            ["security_stamp"] = await userManager.GetSecurityStampAsync(user),
        },
    };

    await authService.SignInAsync(authSession);

    return QueryHelpers.AddQueryString(
        routes.Authorize, AuthorizationRequest.Parameters.RequestUri, requestUri ?? string.Empty);
}
