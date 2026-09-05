using System.Text.Json;
using Abblix.Oidc.Server.Features.ClientInformation;
using Microsoft.EntityFrameworkCore;

namespace AspNetIdentitySample.OidcStore;

/// <summary>
/// Backs the client registry with SQLite instead of the in-memory store seeded from
/// <c>OidcOptions.Clients</c>. Registrations survive restarts, and (once Dynamic Client
/// Registration is enabled) survive across instances too. It implements both client seams:
/// the read side the server consults on every client lookup (<see cref="IClientInfoProvider"/>),
/// and the write side that DCR drives (<see cref="IClientInfoManager"/>).
/// </summary>
/// <remarks>
/// A singleton opening a <c>DbContext</c> per call through <see cref="IDbContextFactory{TContext}"/>,
/// wired after <c>AddOidcServices</c> so it replaces the library's in-memory default.
/// </remarks>
public sealed class DurableClientStore(
    IDbContextFactory<OidcStoreDbContext> contextFactory,
    TimeProvider clock) : IClientInfoProvider, IClientInfoManager
{
    public async Task<ClientInfo?> TryFindClientAsync(string clientId)
    {
        // TODO: TryFindClientAsync is on the hot path: every authorize, every token-endpoint client
        // authentication, every userinfo call resolves the client through here. A production store
        // puts a short-TTL in-memory cache in front (positive entries longer-lived than negative
        // ones, bounded size) so an unknown-client probe cannot spray the database.
        // See https://docs.abblix.com/docs/durable-client-store
        await using var db = await contextFactory.CreateDbContextAsync();

        var record = await db.Clients.FindAsync(clientId);
        return record is null ? null : JsonSerializer.Deserialize<ClientInfo>(record.Payload);
    }

    public Task AddClientAsync(ClientInfo clientInfo) => UpsertAsync(clientInfo);

    // RFC 7592 client update: the client already exists, its metadata changes.
    public Task UpdateClientAsync(ClientInfo clientInfo) => UpsertAsync(clientInfo);

    private async Task UpsertAsync(ClientInfo clientInfo)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        var payload = JsonSerializer.Serialize(clientInfo);
        var existing = await db.Clients.FindAsync(clientInfo.ClientId);
        if (existing is null)
        {
            db.Clients.Add(new PersistedClient
            {
                ClientId = clientInfo.ClientId,
                Payload = payload,
                UpdatedAt = clock.GetUtcNow(),
            });
        }
        else
        {
            existing.Payload = payload;
            existing.UpdatedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync();
    }

    // RFC 7592 deprovisioning: remove a registered client.
    public async Task RemoveClientAsync(string clientId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        var existing = await db.Clients.FindAsync(clientId);
        if (existing is not null)
        {
            db.Clients.Remove(existing);
            await db.SaveChangesAsync();
        }
    }
}

/// <summary>
/// Layers the two client sources on read: a client declared in <c>OidcOptions.Clients</c> wins,
/// and the durable store answers for everything else. This keeps first-party config clients
/// working while the store owns the dynamically registered ones. A DCR registration can therefore
/// never shadow an existing first-party client.
/// </summary>
public sealed class LayeredClientInfoProvider(
    IClientInfoProvider inner,       // library default: clients from OidcOptions.Clients
    DurableClientStore store) : IClientInfoProvider
{
    public async Task<ClientInfo?> TryFindClientAsync(string clientId) =>
        await inner.TryFindClientAsync(clientId) ?? await store.TryFindClientAsync(clientId);
}
