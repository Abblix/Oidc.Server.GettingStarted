using ModelContextProtocol.Authentication;

namespace McpClientApp;

/// <summary>
/// Takes the user through the provider's authorization endpoint and returns what the provider sent to the redirect URI.
/// </summary>
public interface ISignIn
{
    Task<AuthorizationResult?> AuthorizeAsync(AuthorizationCallbackContext context, CancellationToken cancellationToken);
}
