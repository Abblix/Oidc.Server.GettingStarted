using System.Text.Json;
using Abblix.Oidc.Server.Features.ClientInformation;
using Abblix.Oidc.Server.Features.MultiTenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;

namespace AspNetIdentitySample.OidcStore;

/// <summary>
/// Keeps each tenant's client registrations in SQLite instead of the server's in-memory default, so
/// registrations survive restarts, and (once Dynamic Client Registration is enabled) survive across
/// instances too.
/// </summary>
/// <remarks>
/// The server keeps its own client store, which serves the clients a tenant's definition declares ahead of
/// anything here and calls this store for the rest: on every client lookup, and from Dynamic Client
/// Registration and its RFC 7592 lifecycle when that is enabled. So a registration can never shadow a
/// client the tenant declares. Registered as a singleton, since the server's client store that calls it is
/// one; it opens a <c>DbContext</c> per call through <see cref="IDbContextFactory{TContext}"/>.
/// </remarks>
public sealed class DurableClientStore(
    IDbContextFactory<OidcStoreDbContext> contextFactory,
    TimeProvider clock) : ITenantClientRegistrationStore
{
    public async Task<RegisteredClient?> TryFindAsync(TenantDefinition tenant, string clientId)
    {
        // TODO: TryFindAsync is on the hot path: every authorize, every token-endpoint client
        // authentication, every userinfo call for a client the tenant does not declare resolves through
        // here. A production store puts a short-TTL in-memory cache in front, keyed by the tenant's id and
        // generation as well as the client id (positive entries longer-lived than negative ones, bounded
        // size), so an unknown-client probe cannot spray the database.
        // See https://docs.abblix.com/docs/durable-client-store
        await using var db = await contextFactory.CreateDbContextAsync();

        var record = await db.Clients.FindAsync(tenant.Id, tenant.Generation, KeyOf(clientId));
        return record is null ? null : JsonSerializer.Deserialize<RegisteredClient>(record.Payload);
    }

    public async Task<bool> TryAddAsync(TenantDefinition tenant, RegisteredClient client)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        var clientKey = KeyOf(client.ClientInfo.ClientId);
        if (await db.Clients.AnyAsync(row => row.TenantId == tenant.Id
                                             && row.Generation == tenant.Generation
                                             && row.ClientKey == clientKey))
        {
            return false;
        }

        db.Clients.Add(new PersistedClient
        {
            TenantId = tenant.Id,
            Generation = tenant.Generation,
            ClientKey = clientKey,
            TokenId = client.RegistrationAccessTokenId,
            Payload = JsonSerializer.Serialize(client),
            UpdatedAt = clock.GetUtcNow(),
        });

        // The check above answers the common case quietly; the primary key decides a race between two
        // registrations under one id that both passed it: the second insert violates it, and that
        // registration is reported as not added. Any other failure to write is thrown, so a locked or full
        // database is not reported as an id already taken.
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException
                                                  {
                                                      SqliteExtendedErrorCode: raw.SQLITE_CONSTRAINT_PRIMARYKEY,
                                                  })
        {
            return false;
        }
    }

    // RFC 7592 client update: takes effect only while the row carries the token identifier the change was
    // decided on, so of two updates decided at once the second changes nothing.
    public async Task<bool> TryReplaceAsync(TenantDefinition tenant, RegisteredClient current, RegisteredClient updated)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        var clientKey = KeyOf(updated.ClientInfo.ClientId);
        var payload = JsonSerializer.Serialize(updated);
        var updatedAt = clock.GetUtcNow();
        var replaced = await db.Clients
            .Where(row => row.TenantId == tenant.Id
                          && row.Generation == tenant.Generation
                          && row.ClientKey == clientKey
                          && row.TokenId == current.RegistrationAccessTokenId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.TokenId, updated.RegistrationAccessTokenId)
                .SetProperty(row => row.Payload, payload)
                .SetProperty(row => row.UpdatedAt, updatedAt));

        return replaced == 1;
    }

    // RFC 7592 deprovisioning: removes the registration while it carries the token identifier the removal
    // was decided on.
    public async Task<bool> TryRemoveAsync(TenantDefinition tenant, RegisteredClient current)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        var clientKey = KeyOf(current.ClientInfo.ClientId);
        var removed = await db.Clients
            .Where(row => row.TenantId == tenant.Id
                          && row.Generation == tenant.Generation
                          && row.ClientKey == clientKey
                          && row.TokenId == current.RegistrationAccessTokenId)
            .ExecuteDeleteAsync();

        return removed == 1;
    }

    // The server compares client ids without regard to case, so the key does too.
    private static string KeyOf(string clientId) => clientId.ToUpperInvariant();
}
