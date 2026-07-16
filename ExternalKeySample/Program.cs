using System.Security.Cryptography;
using System.Text;
using Abblix.Oidc.Server.Common.Constants;
using Abblix.Oidc.Server.Features;
using Abblix.Oidc.Server.Features.ClientInformation;
using Abblix.Oidc.Server.Features.UserInfo;
using Abblix.Oidc.Server.Azure;
using Abblix.Oidc.Server.MinimalApi;
using Abblix.Oidc.Server.Model;
using Abblix.Oidc.Server.Vault;
using ExternalKeySample;

var builder = WebApplication.CreateBuilder(args);

// This provider has no users: the client_credentials grant issues a token for the client itself. The library
// still requires an IUserInfoProvider, so a no-op satisfies it (it is never invoked here).
builder.Services.AddScoped<IUserInfoProvider, NoUserInfoProvider>();

// The library caches fetched public keys in IMemoryCache and backs its stores with the distributed cache.
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();

// The OIDC endpoints carry CORS metadata (discovery and JWKS are cross-origin by design), so the CORS
// middleware must be present even with no custom policy.
builder.Services.AddCors();

var provider = builder.Configuration.GetSection(ProviderOptions.SectionName).Get<ProviderOptions>()
    ?? new ProviderOptions();

builder.Services.AddOidcMinimalApi(options =>
{
    options.Issuer = provider.Issuer;

    // No SigningKeys / EncryptionKeys are set here on purpose: the custodian's IAuthServiceKeysProvider
    // supplies the public-only external keys, and every private operation runs inside the custodian.
    options.ServiceTokens.AccessToken.Encrypt = provider.EncryptAccessToken;

    options.Scopes = [new ScopeDefinition(provider.Scope)];
    options.Clients =
    [
        new ClientInfo(provider.ClientId)
        {
            ClientSecrets = [new ClientSecret { Sha512Hash = SHA512.HashData(Encoding.UTF8.GetBytes(provider.ClientSecret)) }],
            TokenEndpointAuthMethod = ClientAuthenticationMethods.ClientSecretPost,
            AllowedGrantTypes = [GrantTypes.ClientCredentials],
            AllowedScopes = [provider.Scope],
        },
    ];
});

// Enable token introspection (this both registers the handler and advertises the endpoint). A resource
// server introspects an access token; when EncryptAccessToken is on, that introspection is what drives the
// custodian's remote CEK unwrap, so it is the sample's window onto the encryption arm.
builder.Services.AddIntrospection();

// The custodian is chosen by the KeyCustodian setting. Each package's AddXxxExternalKeys binds its own options
// section, registers the store behind the crypto seam, and replaces the library's default key provider. This MUST
// run after AddOidcMinimalApi so the last singular IAuthServiceKeysProvider registration wins.
var custodian = builder.Configuration.GetValue<KeyCustodian>("KeyCustodian");
switch (custodian)
{
    case KeyCustodian.Vault:
        builder.Services.AddVaultExternalKeys(options => builder.Configuration.GetSection("Vault").Bind(options));
        break;

    case KeyCustodian.Azure:
        builder.Services.AddAzureExternalKeys(options => builder.Configuration.GetSection("Azure").Bind(options));
        break;

    default:
        throw new InvalidOperationException($"Unsupported KeyCustodian '{custodian}'.");
}

var app = builder.Build();

app.UseCors();

// The OIDC protocol endpoints: discovery, JWKS, token, introspection, ...
app.MapOidcEndpoints();

app.Run();
