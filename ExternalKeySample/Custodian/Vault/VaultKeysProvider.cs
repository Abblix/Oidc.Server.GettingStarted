using System.Security.Cryptography;
using Abblix.Jwt;
using Abblix.Oidc.Server.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace ExternalKeySample.Custodian.Vault;

/// <summary>
/// Publishes the public halves of the Transit-held signing and encryption keys to the OIDC pipeline. It never
/// returns private material: each key is public-only, which is precisely the signal the crypto router reads to
/// route the private operation to the Transit port by <c>kid</c>. The same public keys back the <c>/jwks</c>
/// endpoint and local signature verification.
/// </summary>
public sealed class VaultKeysProvider : IAuthServiceKeysProvider
{
    private readonly Lazy<Task<JsonWebKey>> _signingKey;
    private readonly Lazy<Task<JsonWebKey>> _encryptionKey;

    public VaultKeysProvider(VaultTransitClient client, IOptions<VaultTransitOptions> options)
    {
        var settings = options.Value;

        // Public keys are immutable for a given kid (rotation mints a new kid, never edits one), so each is
        // fetched from Transit once at first use and cached for the life of the process.
        _signingKey = new(() => BuildPublicKeyAsync(
            client, settings.SigningKeyName, PublicKeyUsages.Signature, SigningAlgorithms.RS256));
        _encryptionKey = new(() => BuildPublicKeyAsync(
            client, settings.EncryptionKeyName, PublicKeyUsages.Encryption, EncryptionAlgorithms.KeyManagement.RsaOaep256));
    }

    // includePrivateKeys is ignored on purpose: an external key has no private half in this process. Handing
    // the pipeline the public-only key both routes the private operation to Transit (via the missing secret)
    // and gives the JWKS endpoint the exact key clients verify against.
    public async IAsyncEnumerable<JsonWebKey> GetSigningKeys(bool includePrivateKeys = false)
    {
        yield return await _signingKey.Value;
    }

    public async IAsyncEnumerable<JsonWebKey> GetEncryptionKeys(bool includePrivateKeys = false)
    {
        yield return await _encryptionKey.Value;
    }

    private static async Task<JsonWebKey> BuildPublicKeyAsync(
        VaultTransitClient client, string keyName, string usage, string algorithm)
    {
        var pem = await client.GetPublicKeyPemAsync(keyName, CancellationToken.None);

        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);

        // ExportParameters(false) yields public-only parameters, so the JWK carries no private material:
        // HasPrivateKey is false, and the kid is the Transit key name, which is also the custodian's handle.
        return new RsaJsonWebKey { KeyId = keyName, Usage = usage, Algorithm = algorithm }
            .Apply(rsa.ExportParameters(false));
    }
}
