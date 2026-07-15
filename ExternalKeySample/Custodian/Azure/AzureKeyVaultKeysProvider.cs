using System.Security.Cryptography;
using Abblix.Jwt;
using Abblix.Oidc.Server.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace ExternalKeySample.Custodian.Azure;

/// <summary>
/// Publishes the public halves of the Key Vault-held signing and encryption keys to the OIDC pipeline. It never
/// returns private material: each key is public-only, which is the signal the crypto router reads to route the
/// private operation to the Key Vault port by <c>kid</c>. The same public keys back <c>/jwks</c> and local
/// signature verification.
/// </summary>
public sealed class AzureKeyVaultKeysProvider : IAuthServiceKeysProvider
{
    private readonly Lazy<Task<JsonWebKey>> _signingKey;
    private readonly Lazy<Task<JsonWebKey>> _encryptionKey;

    public AzureKeyVaultKeysProvider(AzureKeyVaultClient client, IOptions<AzureKeyVaultOptions> options)
    {
        var settings = options.Value;

        // Public keys are immutable for a given kid, so each is fetched from Key Vault once and cached.
        _signingKey = new(() => BuildPublicKeyAsync(
            client, settings.SigningKeyName, PublicKeyUsages.Signature, SigningAlgorithms.RS256));
        _encryptionKey = new(() => BuildPublicKeyAsync(
            client, settings.EncryptionKeyName, PublicKeyUsages.Encryption, EncryptionAlgorithms.KeyManagement.RsaOaep256));
    }

    // includePrivateKeys is ignored on purpose: an external key has no private half in this process. The
    // public-only key both routes the private operation to Key Vault (via the missing secret) and gives the
    // JWKS endpoint the exact key clients verify against.
    public async IAsyncEnumerable<JsonWebKey> GetSigningKeys(bool includePrivateKeys = false)
    {
        yield return await _signingKey.Value;
    }

    public async IAsyncEnumerable<JsonWebKey> GetEncryptionKeys(bool includePrivateKeys = false)
    {
        yield return await _encryptionKey.Value;
    }

    private static async Task<JsonWebKey> BuildPublicKeyAsync(
        AzureKeyVaultClient client, string keyName, string usage, string algorithm)
    {
        var parameters = await client.GetPublicKeyAsync(keyName, CancellationToken.None);

        // The public-only RSA parameters carry no private material, so HasPrivateKey is false. The kid is the
        // Key Vault key name, which is also the custodian's handle.
        return new RsaJsonWebKey { KeyId = keyName, Usage = usage, Algorithm = algorithm }.Apply(parameters);
    }
}
