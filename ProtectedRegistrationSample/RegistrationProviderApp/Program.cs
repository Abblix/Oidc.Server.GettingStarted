using System.Globalization;
using Abblix.Jwt;
using Abblix.Oidc;
using Abblix.Oidc.Server.Common.Configuration;
using Abblix.Oidc.Server.Common.Constants;
using Abblix.Oidc.Server.Endpoints.DynamicClientManagement.Interfaces;
using Abblix.Oidc.Server.Features;
using Abblix.Oidc.Server.Features.Consents;
using Abblix.Oidc.Server.Features.MultiTenancy;
using Abblix.Oidc.Server.Features.Storages;
using Abblix.Oidc.Server.Features.Tokens.Revocation;
using Abblix.Oidc.Server.MinimalApi;
using RegistrationProviderApp;

const string tenantId = "main";
const string operatorPolicy = "Operator";

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

var issuer = configuration["Provider:Issuer"]
             ?? throw new InvalidOperationException("Provider:Issuer is not configured");

const string maxTokenLifetimeSetting = "Operator:MaxTokenLifetime";
var maxTokenLifetimeValue = configuration[maxTokenLifetimeSetting]
                            ?? throw new InvalidOperationException(
                                $"{maxTokenLifetimeSetting} is not configured: set it to a duration such as 1.00:00:00");
if (!TimeSpan.TryParse(maxTokenLifetimeValue, CultureInfo.InvariantCulture, out var maxTokenLifetime) ||
    maxTokenLifetime <= TimeSpan.Zero)
{
    throw new InvalidOperationException(
        $"{maxTokenLifetimeSetting} is '{maxTokenLifetimeValue}', while it must be a positive duration such as 1.00:00:00");
}

builder.Services.AddSingleton(OperatorKey.FromConfiguration(configuration, "Operator:KeyHash"));

// The provider requires a consent decision even though no end user ever meets it here: registration admits only
// client_credentials (below), a grant with no user behind it, so there is nobody to ask.
builder.Services.AddConsentGrantedForEveryClient();

builder.Services.AddOidcServices(options =>
{
    // Registration is open only to a party holding an initial access token (RFC 7591 section 3). This is already
    // the library's default, written out because it is what the sample is about.
    options.RequireInitialAccessToken = true;

    // The parties an operator approves here are services and internal tools, which act for themselves. A
    // registration asking for a grant that signs a user in is refused, so this provider needs no login page.
    options.NewClientOptions = new NewClientOptions
    {
        AllowedGrantTypes = [GrantTypes.ClientCredentials],
    };

    options.Tenants.Add(new TenantDefinition
    {
        Id = tenantId,
        Issuer = new Uri(issuer),

        // No pre-registered clients: every client arrives through registration with an initial access token
        Clients = [],

        SigningKeys = [JsonWebKeyFactory.CreateRsa(PublicKeyUsages.Signature)],
    });
});

// Serves the registration endpoint and the client configuration endpoint (RFC 7592), advertises the first as
// registration_endpoint in the metadata, and registers IInitialAccessTokenService
builder.Services.AddDynamicClientRegistration();

// The operator endpoints accept only the operator key. A separate command-line tool could not stand in for them:
// this provider keeps its signing key and its token registry in memory, so a tool running in another process
// would sign tokens this provider rejects and record revocations it never reads.
builder.Services
    .AddAuthentication()
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, OperatorKeyAuthenticationHandler>(
        OperatorKeyAuthenticationHandler.SchemeName, configureOptions: null);

builder.Services.AddAuthorization(options => options.AddPolicy(operatorPolicy, policy => policy
    .AddAuthenticationSchemes(OperatorKeyAuthenticationHandler.SchemeName)
    .RequireAuthenticatedUser()));

var app = builder.Build();

app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapOidcEndpoints();

var operatorApi = app.MapGroup("/operator/initial-access-tokens").RequireAuthorization(operatorPolicy);

// Issues an initial access token for one party. The token can register clients until it expires or is revoked;
// the jti in the response is what revokes it.
operatorApi.MapPost("", async (
    IssueInitialAccessTokenRequest request,
    IInitialAccessTokenService tokenService,
    ITenantCatalog tenants,
    TimeProvider clock,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Subject))
        return Results.BadRequest(new { error = "subject names the party the token is issued to and is required" });

    if (request.ExpiresIn is not > 0 || TimeSpan.FromSeconds(request.ExpiresIn.Value) > maxTokenLifetime)
    {
        return Results.BadRequest(new
        {
            error = $"expires_in is the token lifetime in seconds, from 1 to {(int)maxTokenLifetime.TotalSeconds}",
        });
    }

    var tenant = await FindTenantAsync(tenants, cancellationToken);
    var token = await tokenService.IssueTokenAsync(
        tenant, request.Subject, clock.GetUtcNow(), TimeSpan.FromSeconds(request.ExpiresIn.Value));

    return Results.Ok(new
    {
        initial_access_token = token.EncodedJwt,
        jti = token.Token.Payload.JwtId,
        expires_at = token.Token.Payload.ExpiresAt,
    });
});

// Revokes an initial access token by its jti. The registration endpoint reads the token registry of the tenant
// that issued the token, so the revocation is recorded there. A token's lifetime is not known from its jti alone,
// so the record is kept for the longest lifetime this endpoint issues, which outlives any token it could refuse.
// The answer is the same whether or not a token with this jti was ever issued: revoking is idempotent.
operatorApi.MapDelete("/{jti}", async (
    string jti,
    ITokenRegistry tokenRegistry,
    ITenantCatalog tenants,
    TimeProvider clock,
    CancellationToken cancellationToken) =>
{
    var tenant = await FindTenantAsync(tenants, cancellationToken);
    await tokenRegistry.SetStatusAsync(tenant, jti, JsonWebTokenStatus.Revoked, clock.GetUtcNow() + maxTokenLifetime);
    return Results.NoContent();
});

app.Run();

static async Task<TenantDefinition> FindTenantAsync(ITenantCatalog tenants, CancellationToken cancellationToken)
    => await tenants.FindByIdAsync(tenantId, cancellationToken)
       ?? throw new InvalidOperationException($"The tenant '{tenantId}' is not registered");
