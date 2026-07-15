using Abblix.Jwt;
using Abblix.Jwt.Signing;

namespace ExternalKeySample.Custodian.Azure;

/// <summary>
/// Signs tokens with a private key held in Azure Key Vault. The library calls this for any signing key it holds
/// public-only, passing the key's published <c>kid</c> as the Key Vault key name. The private key never enters
/// this process.
/// </summary>
public sealed class AzureKeyVaultSigner(AzureKeyVaultClient client) : IExternalSigner
{
    public async ValueTask<byte[]> SignAsync(string kid, string algorithm, byte[] data, CancellationToken cancellationToken)
    {
        // This sample provisions an RSA key and signs RS256, whose signature is already in JWS wire format. An
        // EC key would sign ES256; Azure Key Vault returns EC signatures as R||S, so it would need no DER
        // conversion, unlike some other custodians.
        if (algorithm != SigningAlgorithms.RS256)
            throw new NotSupportedException(
                $"The Azure custodian in this sample signs {SigningAlgorithms.RS256} only; got '{algorithm}'.");

        return await client.SignAsync(kid, data, cancellationToken);
    }
}
