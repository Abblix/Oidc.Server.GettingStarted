using System.Text.Json;
using Abblix.Jwt;
using Abblix.Oidc.Server.Common.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace AspNetIdentitySample.OidcStore;

/// <summary>
/// Loads the OIDC signing keys from the database instead of generating a fresh key at every
/// startup. Because the same key is reloaded on restart, tokens issued before the restart still
/// validate: the exact failure mode that the ephemeral <see cref="JsonWebKeyFactory.CreateRsa"/>
/// default runs into (a rolling restart signs new tokens with a new kid and orphans every token
/// already in circulation).
/// </summary>
/// <remarks>
/// Registered as a singleton after <c>AddOidcServices</c>, so it overrides the library default
/// (<c>OidcOptionsKeysProvider</c>). A singleton cannot hold a scoped <c>DbContext</c>, so it
/// opens one per call through <see cref="IDbContextFactory{TContext}"/>.
/// </remarks>
public sealed class DatabaseKeysProvider(
    IDbContextFactory<OidcStoreDbContext> contextFactory,
    IDataProtectionProvider dataProtection) : IAuthServiceKeysProvider
{
    // The Data Protection purpose string binds the protector to this use. Seeding (which writes the
    // key) and this provider (which reads it) must create the protector with the identical purpose.
    public const string SigningKeyProtectorPurpose = "AspNetIdentitySample.OidcStore.SigningKeys.v1";

    private readonly IDataProtector _protector = dataProtection.CreateProtector(SigningKeyProtectorPurpose);

    public async IAsyncEnumerable<JsonWebKey> GetSigningKeys(bool includePrivateKeys = false)
    {
        // TODO: this reads the database on every token-signing operation. Keys rotate on human
        // timescales, not per request, so a production provider caches the result in-process for a
        // short TTL and invalidates on a rotation signal. Left uncached here to keep the seam obvious.
        // See https://docs.abblix.com/docs/signing-key-persistence
        await using var db = await contextFactory.CreateDbContextAsync();

        var records = await db.SigningKeys
            .Where(key => key.IsActive && key.Usage == PublicKeyUsages.Signature)
            .ToListAsync();

        foreach (var record in records)
        {
            // Undo the at-rest Data Protection encryption applied when the key was stored. This throws
            // if the Data Protection key ring that wrote the key is gone: that ring is now as critical
            // to persist and protect as the signing key itself.
            var jwkJson = _protector.Unprotect(record.JwkJson);

            var jwk = JsonSerializer.Deserialize<JsonWebKey>(jwkJson)
                ?? throw new InvalidOperationException(
                    $"Signing key '{record.KeyId}' does not deserialize to a valid JWK.");

            // Sanitize drops the private half unless the caller asks for it: the signing pipeline
            // requests private keys, the JWKS endpoint requests public-only. Mirrors the default provider.
            yield return jwk.Sanitize(includePrivateKeys);
        }
    }

    // This sample issues no encrypted tokens, so it stores and returns no encryption keys.
    public async IAsyncEnumerable<JsonWebKey> GetEncryptionKeys(bool includePrivateKeys = false)
    {
        await Task.CompletedTask;
        yield break;
    }
}
