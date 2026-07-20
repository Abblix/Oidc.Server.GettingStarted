using System.Security.Cryptography;
using System.Text;
using Abblix.Oidc.Server.Common.Constants;
using Abblix.Oidc.Server.Features;
using Abblix.Oidc.Server.Features.ClientInformation;
using Abblix.Jwt.ExternalKeys;
using Abblix.Oidc.Server.Features.ExternalKeys;
using Abblix.Oidc.Server.Features.UserInfo;
using Abblix.Jwt.Azure;
using Abblix.Oidc.Server.MinimalApi;
using Abblix.Oidc.Server.Model;
using Abblix.Jwt.Vault;
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

// Wiring a custodian is two steps, and the sample splits along them. First: WHICH custodian holds the keys. The
// KeyCustodian setting names it, and each member's name is also its configuration section, so the section name is
// never repeated here. Only the call itself differs, since each package has its own options type. This MUST run
// after AddOidcMinimalApi, because the placement call below composes the external crypto backends with the in-process
// ones the OIDC registration puts in place.
var custodian = builder.Configuration.GetValue<KeyCustodian>("KeyCustodian");
var placement = builder.Configuration.GetValue<UseKeysIn>("UseKeysIn");
var settings = builder.Configuration.GetSection(custodian.ToString());
var custodianBuilder = custodian switch
{
    KeyCustodian.Vault => builder.Services.AddVaultCustodian(settings.Bind),
    KeyCustodian.Azure => builder.Services.AddAzureCustodian(settings.Bind),
    _ => throw new InvalidOperationException($"Unsupported KeyCustodian '{custodian}'."),
};

// Second: HOW the library uses it. This is the security posture, so it is named rather than defaulted; omitting it
// fails at startup rather than falling back to local keys. The two placements differ in whether the private half ever
// exists in this process, and the sample picks between them with the UseKeysIn setting.
switch (placement)
{
    case UseKeysIn.Custodian:
        // The private half never enters this process: each key name is the custodian's own, and every signature
        // and CEK unwrap is a round-trip to it.
        custodianBuilder.UseKeysInCustodian(new CustodianHeldKeys
        {
            SigningKeyName = provider.SigningKeyName,

            // In general an encryption key serves two purposes: encrypting the provider's own tokens, and
            // decrypting inbound JWE a client sent (an encrypted request object or client assertion). This sample
            // only ever needs the first, since client_credentials sends neither, so it names the key only while it
            // encrypts its tokens. Left unset, no encryption key is published at all.
            EncryptionKeyName = provider.EncryptAccessToken ? provider.EncryptionKeyName : null,
        });
        break;

    case UseKeysIn.Process:
        // The server mints its own signing keys, seals each to the custodian's key-encryption key, and keeps the
        // sealed copies in a store the same backend provides. Signing then runs locally; the custodian is reached
        // only to open a sealed key and to protect the next one. The private half lives in memory while in use, so
        // this trades the custodian-held placement's "never in the process" for no per-token round-trip.
        var minted = custodianBuilder.UseKeysInProcess(new MintedKeys
        {
            KeyEncryptionKeyName = provider.KeyEncryptionKeyName,

            // Config drives this directly: it is null unless the configuration sets it, and null mints no
            // encryption key, so it is left out of appsettings.json until this sample encrypts its own tokens.
            EncryptionAlgorithm = provider.EncryptionAlgorithm,
        });

        // The ring of sealed keys lives in the same backend as the custodian: a KV v2 engine for Vault, a Blob
        // Storage container for Azure. Each defaults its location, so the Vault ring needs no configuration and
        // the Azure ring needs only the storage endpoint (the Azure:Blob section).
        _ = custodian switch
        {
            KeyCustodian.Vault => minted.PersistRingToVaultKeyValue(),
            KeyCustodian.Azure => minted.PersistRingToAzureBlob(settings.GetSection("Blob").Bind),
            _ => throw new InvalidOperationException($"Unsupported KeyCustodian '{custodian}'."),
        };
        break;

    default:
        throw new InvalidOperationException($"Unsupported UseKeysIn '{placement}'.");
}

var app = builder.Build();

app.UseCors();

// The OIDC protocol endpoints: discovery, JWKS, token, introspection, ...
app.MapOidcEndpoints();

app.Run();
