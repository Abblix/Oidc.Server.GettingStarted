using Abblix.SecurityEvents.BackChannelLogout;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace TestClientApp.BackChannelLogout;

/// <summary>
/// The provider sessions a logout token ended: the receiver hands each validated token here (Back-Channel Logout 1.0
/// section 2.7). The sign-in cookie lives in the browser, out of reach of the provider's server-to-server call, so the
/// session is recorded here and the cookie's validation refuses it on the browser's next request.
/// </summary>
/// <remarks>
/// Kept in memory, so it holds for one instance and until a restart, which suits a sample. Several instances need a
/// shared store, such as a distributed cache.
/// </remarks>
public sealed class RevokedSessions(
    IMemoryCache cache,
    IOptionsMonitor<CookieAuthenticationOptions> cookieOptions,
    ILogger<RevokedSessions> logger) : ILogoutNotificationSink
{
    public Task<string?> ConsumeAsync(LogoutNotification notification, CancellationToken cancellationToken)
    {
        // This client registered backchannel_logout_session_required, so a token names the one sign-in to end. One that
        // names only the user would mean every sign-in of that user here, which this client does not track.
        if (notification.SessionId is not { } sessionId)
            return Task.FromResult<string?>("The logout token names no session, and this client ends sessions by sid.");

        // A cookie unused for its lifetime expires on its own, so the entry need not outlive that.
        var lifetime = cookieOptions.Get(CookieAuthenticationDefaults.AuthenticationScheme).ExpireTimeSpan;
        cache.Set(Key(sessionId), true, lifetime);

        logger.LogInformation("A back-channel logout token ended a provider session; a sign-in made through it ends on its next request");
        return Task.FromResult<string?>(null);
    }

    public bool IsRevoked(string sessionId) => cache.TryGetValue(Key(sessionId), out _);

    private static string Key(string sessionId) => $"revoked-session:{sessionId}";
}
