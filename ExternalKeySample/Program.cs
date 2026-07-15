using System.Security.Cryptography;
using System.Text;
using Abblix.Oidc.Server.Common.Constants;
using Abblix.Oidc.Server.Features;
using Abblix.Oidc.Server.Features.ClientInformation;
using Abblix.Oidc.Server.Features.UserInfo;
using Abblix.Oidc.Server.MinimalApi;
using Abblix.Oidc.Server.Model;
using ExternalKeySample;
using ExternalKeySample.Custodian.Azure;
using ExternalKeySample.Custodian.Vault;

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

// The custodian is chosen by the KeyCustodian setting. This registration MUST run after AddOidcMinimalApi: its
// IAuthServiceKeysProvider replaces the library default, and the last singular registration wins.
var custodian = builder.Configuration.GetValue<KeyCustodian>("KeyCustodian");
switch (custodian)
{
    case KeyCustodian.Vault:
        builder.Services.AddVaultCustodian(builder.Configuration);
        break;

    case KeyCustodian.Azure:
        builder.Services.AddAzureCustodian(builder.Configuration);
        break;

    default:
        throw new InvalidOperationException($"Unsupported KeyCustodian '{custodian}'.");
}

var app = builder.Build();

app.UseCors();

// The OIDC protocol endpoints: discovery, JWKS, token, introspection, ...
app.MapOidcEndpoints();

app.Run();
