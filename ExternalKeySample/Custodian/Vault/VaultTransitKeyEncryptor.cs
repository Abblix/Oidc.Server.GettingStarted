using Abblix.Jwt;
using Abblix.Jwt.Encryption;

namespace ExternalKeySample.Custodian.Vault;

/// <summary>
/// Recovers encrypted-token Content Encryption Keys with a private key held inside Vault / OpenBao Transit.
/// Only the private step is remote: the library wraps the CEK with the recipient's public half in process, so
/// this port implements just <see cref="UnwrapKeyAsync"/>. The other operations exist on the seam for
/// symmetric and ECDH custodians and are unreachable in this RSA-OAEP sample.
/// </summary>
public sealed class VaultTransitKeyEncryptor(VaultTransitClient client) : IExternalKeyEncryptor
{
    public async ValueTask<byte[]?> UnwrapKeyAsync(
        string kid, string algorithm, JsonWebTokenHeader header, byte[] encryptedKey, CancellationToken cancellationToken)
    {
        // The published encryption key is RSA-OAEP-256, so the library only ever asks to unwrap that. Transit's
        // RSA decrypt uses OAEP-SHA256, which is exactly what RSA-OAEP-256 produces, so a standard JWE
        // ciphertext round-trips once the client frames it in Transit's envelope.
        if (algorithm != EncryptionAlgorithms.KeyManagement.RsaOaep256)
            throw new NotSupportedException(
                $"The Vault custodian in this sample unwraps {EncryptionAlgorithms.KeyManagement.RsaOaep256} only; got '{algorithm}'.");

        return await client.DecryptAsync(kid, encryptedKey, cancellationToken);
    }

    // Wrapping an RSA CEK uses the recipient's public half, so the library performs it in process and never
    // calls this. It is part of the seam for symmetric (AES-KW) custodians.
    public ValueTask<byte[]> WrapKeyAsync(
        string kid, string algorithm, JsonWebTokenHeader header, byte[] contentEncryptionKey, CancellationToken cancellationToken)
        => throw new NotSupportedException("Symmetric key wrapping is not used by this RSA-OAEP sample.");

    // ECDH-ES key agreement: only for EC encryption keys, which this sample does not provision.
    public ValueTask<byte[]> AgreeKeyAsync(
        string kid, string algorithm, JsonWebKey ephemeralPublicKey, CancellationToken cancellationToken)
        => throw new NotSupportedException("ECDH-ES key agreement is not used by this RSA-OAEP sample.");

    // The generic symmetric seal/open pair backs reversible-subject protection under a symmetric custodian key.
    // This sample protects nothing symmetrically, so neither is reached.
    public ValueTask<byte[]> SealAsync(string kid, byte[] plaintext, byte[] associatedData, CancellationToken cancellationToken)
        => throw new NotSupportedException("Symmetric sealing is not used by this RSA-OAEP sample.");

    public ValueTask<byte[]?> OpenAsync(string kid, byte[] sealedData, byte[] associatedData, CancellationToken cancellationToken)
        => throw new NotSupportedException("Symmetric opening is not used by this RSA-OAEP sample.");
}
