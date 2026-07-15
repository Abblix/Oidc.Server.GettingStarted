using Abblix.Jwt;
using Abblix.Jwt.Encryption;

namespace ExternalKeySample.Custodian.Azure;

/// <summary>
/// Recovers encrypted-token Content Encryption Keys with a private key held in Azure Key Vault. Only the private
/// step is remote: the library wraps the CEK with the recipient's public half in process, so this port
/// implements just <see cref="UnwrapKeyAsync"/>. The remaining operations exist on the seam for symmetric and
/// ECDH custodians and are unreachable in this RSA-OAEP sample.
/// </summary>
public sealed class AzureKeyVaultKeyEncryptor(AzureKeyVaultClient client) : IExternalKeyEncryptor
{
    public async ValueTask<byte[]?> UnwrapKeyAsync(
        string kid, string algorithm, JsonWebTokenHeader header, byte[] encryptedKey, CancellationToken cancellationToken)
    {
        if (algorithm != EncryptionAlgorithms.KeyManagement.RsaOaep256)
            throw new NotSupportedException(
                $"The Azure custodian in this sample unwraps {EncryptionAlgorithms.KeyManagement.RsaOaep256} only; got '{algorithm}'.");

        return await client.DecryptAsync(kid, encryptedKey, cancellationToken);
    }

    public ValueTask<byte[]> WrapKeyAsync(
        string kid, string algorithm, JsonWebTokenHeader header, byte[] contentEncryptionKey, CancellationToken cancellationToken)
        => throw new NotSupportedException("Symmetric key wrapping is not used by this RSA-OAEP sample.");

    public ValueTask<byte[]> AgreeKeyAsync(
        string kid, string algorithm, JsonWebKey ephemeralPublicKey, CancellationToken cancellationToken)
        => throw new NotSupportedException("ECDH-ES key agreement is not used by this RSA-OAEP sample.");

    public ValueTask<byte[]> SealAsync(string kid, byte[] plaintext, byte[] associatedData, CancellationToken cancellationToken)
        => throw new NotSupportedException("Symmetric sealing is not used by this RSA-OAEP sample.");

    public ValueTask<byte[]?> OpenAsync(string kid, byte[] sealedData, byte[] associatedData, CancellationToken cancellationToken)
        => throw new NotSupportedException("Symmetric opening is not used by this RSA-OAEP sample.");
}
