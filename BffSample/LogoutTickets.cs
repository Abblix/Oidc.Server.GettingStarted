using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace BffSample;

/// <summary>
/// Keeps the provider's end-session address on the server between the logout POST and the browser's navigation to
/// it. The address carries the ID token as id_token_hint, and RFC 10017 section 6.1.4.2 counts on the BFF exposing
/// no token to the browser-based application, so the SPA is handed a ticket that stands for the address instead.
/// </summary>
/// <param name="cache">Where the addresses wait for their tickets.</param>
/// <param name="lifetime">How long a ticket waits for the browser's navigation before it is gone.</param>
public sealed class LogoutTickets(IMemoryCache cache, TimeSpan lifetime)
{
    private const int TicketBytes = 32;
    private const string KeyPrefix = "logout-ticket:";

    /// <summary>
    /// Stores <paramref name="endSessionUrl"/> and returns the ticket that redeems it once.
    /// </summary>
    public string Issue(string endSessionUrl)
    {
        var ticket = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TicketBytes));
        cache.Set(KeyPrefix + ticket, new Entry(endSessionUrl), lifetime);
        return ticket;
    }

    /// <summary>
    /// The address <paramref name="ticket"/> stands for, or null when the ticket is unknown, expired or already used.
    /// </summary>
    public string? Redeem(string ticket)
    {
        var key = KeyPrefix + ticket;
        if (!cache.TryGetValue<Entry>(key, out var entry) || entry == null)
            return null;

        cache.Remove(key);

        // Two requests carrying the same ticket may both find the entry before either removes it; only the first
        // to claim it gets the address
        return Interlocked.Exchange(ref entry.Claimed, 1) == 0 ? entry.EndSessionUrl : null;
    }

    private sealed class Entry(string endSessionUrl)
    {
        public string EndSessionUrl { get; } = endSessionUrl;
        public int Claimed;
    }
}
