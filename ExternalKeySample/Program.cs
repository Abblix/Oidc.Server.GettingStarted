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

// Every value the sample cannot invent carries no default here: a default in code for the client secret's hash
// would be credential material in code, and a default for a name or a URL is a second copy of a line
// appsettings.json already holds. `required` says nothing about that: the binder ignores it, so which settings a
// configuration must carry is decided here and not by the keyword. Each value passes through Required where it is
// USED rather than in a list up front, because several are read in one posture only and a list would refuse a
// configuration that is complete for the other. A member read in one posture only says so in its own summary.
var provider = builder.Configuration.GetSection(ProviderOptions.SectionName).Get<ProviderOptions>()
    ?? throw new InvalidOperationException($"The '{ProviderOptions.SectionName}' section is not configured.");

// AddOidcServices is the core plus this package's transport, and the MVC package spells it identically, so
// swapping adapters changes the package reference and the endpoint mapping rather than this line.
builder.Services.AddOidcServices(options =>
{
    options.Issuer = Required(nameof(provider.Issuer), provider.Issuer);

    // No SigningKeys / EncryptionKeys are set here on purpose: the custodian's IAuthServiceKeysProvider
    // supplies the public-only external keys, and every private operation runs inside the custodian.
    options.ServiceTokens.AccessToken.Encrypt = provider.EncryptAccessToken;

    options.Scopes = [new ScopeDefinition(Required(nameof(provider.Scope), provider.Scope))];
    options.Clients =
    [
        new ClientInfo(Required(nameof(provider.ClientId), provider.ClientId))
        {
            ClientSecrets =
            [
                new ClientSecret
                {
                    Sha512Hash = DecodeSecretHash(
                        Required(nameof(provider.ClientSecretSha512Hash), provider.ClientSecretSha512Hash)),
                },
            ],
            TokenEndpointAuthMethod = ClientAuthenticationMethods.ClientSecretPost,
            AllowedGrantTypes = [GrantTypes.ClientCredentials],
            AllowedScopes = [Required(nameof(provider.Scope), provider.Scope)],
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
// after AddOidcServices, because the placement call below composes the external crypto backends with the in-process
// ones the OIDC registration puts in place. Both settings are read as nullable and refused when absent: bound to
// the enum directly, a missing one takes the first member, picking a security posture by declaration order.
var custodian = builder.Configuration.GetValue<KeyCustodian?>("KeyCustodian")
    ?? throw new InvalidOperationException("KeyCustodian is not configured.");
var placement = builder.Configuration.GetValue<UseKeysIn?>("UseKeysIn")
    ?? throw new InvalidOperationException("UseKeysIn is not configured.");
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
            SigningKeyName = Required(nameof(provider.SigningKeyName), provider.SigningKeyName),

            // In general an encryption key serves two purposes: encrypting the provider's own tokens, and
            // decrypting inbound JWE a client sent (an encrypted request object or client assertion). This sample
            // only ever needs the first, since client_credentials sends neither, so it names the key only while it
            // encrypts its tokens. Left unset, no encryption key is published at all.
            EncryptionKeyName = provider.EncryptAccessToken
                ? Required(nameof(provider.EncryptionKeyName), provider.EncryptionKeyName)
                : null,
        });
        break;

    case UseKeysIn.Process:
        // The server mints its own signing keys, seals each to the custodian's key-encryption key, and keeps the
        // sealed copies in a store the same backend provides. Signing then runs locally; the custodian is reached
        // only to open a sealed key and to protect the next one. The private half lives in memory while in use, so
        // this trades the custodian-held placement's "never in the process" for no per-token round-trip.
        var minted = custodianBuilder.UseKeysInProcess(new MintedKeys
        {
            KeyEncryptionKeyName = Required(nameof(provider.KeyEncryptionKeyName), provider.KeyEncryptionKeyName),

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

// Stands where the value is consumed rather than in a list of its own, so a setting that stops being read cannot
// leave a check behind demanding it. The message names the section and the member, which is what the reader needs
// and what the framework's own null reference will not say.
static string Required(string name, string? value) =>
    !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException($"'{ProviderOptions.SectionName}:{name}' is not configured.");

// Named rather than inline so a mistyped setting says which one it is: the framework's own FormatException for
// base64 names neither the setting nor this sample, and the likeliest mistake is writing the secret itself here.
static byte[] DecodeSecretHash(string value)
{
    try
    {
        return Convert.FromBase64String(value);
    }
    catch (FormatException exception)
    {
        throw new InvalidOperationException(
            $"'{ProviderOptions.SectionName}:{nameof(ProviderOptions.ClientSecretSha512Hash)}' is not base64. " +
            "It holds the base64 SHA-512 hash of the client secret, not the secret.",
            exception);
    }
}
