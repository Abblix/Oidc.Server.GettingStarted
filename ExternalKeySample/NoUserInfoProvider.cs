using System.Text.Json.Nodes;
using Abblix.Oidc.Server.Features.UserAuthentication;
using Abblix.Oidc.Server.Features.UserInfo;

namespace ExternalKeySample;

/// <summary>
/// The client_credentials grant issues a token for the client itself, not for a user, so no user claims are
/// ever resolved. The library still requires an <see cref="IUserInfoProvider"/> to be registered, so this
/// no-op satisfies the contract; it is never actually invoked in this sample.
/// </summary>
public sealed class NoUserInfoProvider : IUserInfoProvider
{
    public Task<JsonObject?> GetUserInfoAsync(AuthSession authSession, IEnumerable<string> requestedClaims)
        => Task.FromResult<JsonObject?>(null);
}
