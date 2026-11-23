using Abblix.Oidc.Server.Common.Configuration;
using Abblix.Oidc.Server.Endpoints.Authorization.Interfaces;
using Abblix.Oidc.Server.Features.Consents;
using Abblix.Oidc.Server.Features.MultiTenancy;
using Abblix.Oidc.Server.Features.UserAuthentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ProviderApp;

/// <summary>
/// The consent the user gives or refuses on the consent page, for one authorization request at a time.
/// </summary>
/// <remarks>
/// Every client of this provider registered itself, so nothing assures the provider who is asking, and RFC 10017
/// section 6.3.3.2, repeating RFC 6749 section 10.2, says the authorization server "SHOULD NOT process
/// authorization requests automatically without user consent or interaction, except when the authorization server
/// can assure the identity of the client application". The section lets an earlier approval stand only for a
/// client restricted to exact absolute HTTPS redirect URIs, and an MCP client redirects to a loopback http
/// address. So a decision answers the one request it was given for and is gone once read: the next authorization,
/// from the same client or any other, goes to the consent page again. A decision whose request never comes back
/// expires at most one stored-request lifetime after the user answered.
/// </remarks>
/// <param name="cache">Where decisions wait for their requests to resume.</param>
/// <param name="options">The server's options, for how long a stored authorization request lives.</param>
/// <param name="clock">Stamps a decision, so a request asking for consent with prompt=consent sees it given after
/// the consent page was opened.</param>
public sealed class ConsentPageDecisions(IMemoryCache cache, IOptions<OidcOptions> options, TimeProvider clock)
    : IUserConsentsProvider
{
    /// <summary>
    /// Records the user's answer on the consent page for the request stored under <paramref name="requestUri"/>.
    /// </summary>
    public void Record(TenantDefinition tenant, AuthSession authSession, Uri requestUri, bool granted)
        => cache.Set(
            Key(tenant, authSession, requestUri),
            new Decision(granted, clock.GetUtcNow()),
            options.Value.AuthenticationRequestExpiresIn);

    /// <inheritdoc />
    public Task<UserConsents> GetUserConsentsAsync(
        TenantDefinition tenant, ValidAuthorizationRequest request, AuthSession authSession)
    {
        var asked = new ConsentDefinition(request.Scope, request.Resources)
        {
            AuthorizationDetails = request.AuthorizationDetails,
        };

        // The request comes back from the consent page under the request_uri the page was opened with
        if (request.RequestUri is not { } requestUri ||
            !cache.TryGetValue<Decision>(Key(tenant, authSession, requestUri), out var decision) ||
            decision == null)
        {
            return Task.FromResult(new UserConsents { Pending = asked });
        }

        cache.Remove(Key(tenant, authSession, requestUri));

        // Two resumes of the same request may both find the decision before either removes it; only the first to
        // claim it is answered with it, the other goes to the consent page again
        if (Interlocked.Exchange(ref decision.Claimed, 1) != 0)
            return Task.FromResult(new UserConsents { Pending = asked });

        // A consent that grants nothing and leaves nothing pending is the user's refusal: the client gets
        // access_denied
        return Task.FromResult(decision.Granted
            ? new UserConsents { Granted = asked, GivenAt = decision.GivenAt }
            : new UserConsents { GivenAt = decision.GivenAt });
    }

    private static DecisionKey Key(TenantDefinition tenant, AuthSession authSession, Uri requestUri)
        => new(tenant.Id, authSession.SessionId, requestUri.OriginalString);

    private sealed record DecisionKey(string TenantId, string SessionId, string RequestUri);

    private sealed class Decision(bool granted, DateTimeOffset givenAt)
    {
        public bool Granted { get; } = granted;
        public DateTimeOffset GivenAt { get; } = givenAt;
        public int Claimed;
    }
}
