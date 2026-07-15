using Abblix.Jwt;
using Abblix.Jwt.Signing;

namespace ExternalKeySample.Custodian.Vault;

/// <summary>
/// Signs tokens with a private key held inside Vault / OpenBao Transit. The library calls this for any signing
/// key it holds public-only (no private half in the key store), passing the key's published <c>kid</c> as the
/// Transit key name. The private key never enters this process; only the signing input and the resulting
/// signature cross the boundary.
/// </summary>
public sealed class VaultTransitSigner(VaultTransitClient client) : IExternalSigner
{
    public async ValueTask<byte[]> SignAsync(string kid, string algorithm, byte[] data, CancellationToken cancellationToken)
    {
        // This sample provisions an RSA key and signs RS256. RSA signatures are already in JWS wire format
        // (raw bytes), so nothing but the version-prefix strip in the client is needed. An EC key would sign
        // ES256 and need the DER -> R||S conversion RFC 7518 Section 3.4 mandates.
        if (algorithm != SigningAlgorithms.RS256)
            throw new NotSupportedException(
                $"The Vault custodian in this sample signs {SigningAlgorithms.RS256} only; got '{algorithm}'.");

        return await client.SignAsync(kid, data, cancellationToken);
    }
}
