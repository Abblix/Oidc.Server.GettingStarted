using System.Collections.Concurrent;
using Abblix.Oidc.Server.Endpoints.Authorization.Interfaces;
using Abblix.Oidc.Server.Features.Consents;
using Abblix.Oidc.Server.Features.MultiTenancy;
using Abblix.Oidc.Server.Features.UserAuthentication;

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
/// from the same client or any other, goes to the consent page again.
/// </remarks>
public sealed class ConsentPageDecisions(TimeProvider clock) : IUserConsentsProvider
{
    private readonly ConcurrentDictionary<(string TenantId, string SessionId, string RequestUri), Decision> _decisions
        = new();

    /// <summary>
    /// Records the user's answer on the consent page for the request stored under <paramref name="requestUri"/>.
    /// </summary>
    public void Record(TenantDefinition tenant, AuthSession authSession, Uri requestUri, bool granted)
        => _decisions[(tenant.Id, authSession.SessionId, requestUri.OriginalString)] =
            new Decision(granted, clock.GetUtcNow());

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
            !_decisions.TryRemove((tenant.Id, authSession.SessionId, requestUri.OriginalString), out var decision))
        {
            return Task.FromResult(new UserConsents { Pending = asked });
        }

        // A consent that grants nothing and leaves nothing pending is the user's refusal: the client gets
        // access_denied
        return Task.FromResult(decision.Granted
            ? new UserConsents { Granted = asked, GivenAt = decision.GivenAt }
            : new UserConsents { GivenAt = decision.GivenAt });
    }

    private sealed record Decision(bool Granted, DateTimeOffset GivenAt);
}
