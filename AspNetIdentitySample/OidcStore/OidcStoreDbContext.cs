using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace AspNetIdentitySample.OidcStore;

/// <summary>
/// Durable storage for the OpenID Connect protocol state that must outlive a process:
/// the signing keys and the client registrations. Kept in its own database, separate from
/// the Identity user store, so the two concerns (who the users are versus how the protocol
/// is configured) do not share a schema.
/// </summary>
public class OidcStoreDbContext(DbContextOptions<OidcStoreDbContext> options) : DbContext(options)
{
    public DbSet<PersistedSigningKey> SigningKeys => Set<PersistedSigningKey>();
    public DbSet<PersistedClient> Clients => Set<PersistedClient>();
}

/// <summary>
/// A signing key at rest. The full JWK, private material included, is serialized as JSON and
/// keyed by its <c>kid</c>, so a restart reloads the same key and tokens issued before the
/// restart still validate.
/// </summary>
public class PersistedSigningKey
{
    [Key]
    public required string KeyId { get; set; }

    // "sig" or "enc": this sample only ever stores signing keys.
    public required string Usage { get; set; }

    // The signing key JWK, encrypted at rest with ASP.NET Data Protection (ciphertext, not raw JSON).
#warning JwkJson stores the signing key ENCRYPTED with ASP.NET Data Protection, which RELOCATES rather than removes the custody problem: the Data Protection key ring must now itself be persisted, shared across instances, and protected (PersistKeysTo + ProtectKeysWith a cert or KMS), or it can lose your signing keys on its own rotation. The real home for signing keys is an HSM or KMS, not a database. See https://docs.abblix.com/docs/signing-key-persistence#encrypting-the-stored-key-at-rest
    public required string JwkJson { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A client registration at rest. <c>ClientInfo</c> carries sixty-plus members, so it is stored
/// as one JSON document rather than a column per property: the server only ever looks a client
/// up by id, and a column-per-property schema would demand a migration on every new policy knob.
/// </summary>
public class PersistedClient
{
    [Key]
    public required string ClientId { get; set; }

#warning Payload embeds client secret hashes, and for client_secret_jwt clients the raw secret. Treat this store like a password database (encrypted disk, TLS, a least-privilege role, guarded backups) before production. See https://docs.abblix.com/docs/durable-client-store
    public required string Payload { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
