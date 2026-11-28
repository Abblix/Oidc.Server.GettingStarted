using ModelContextProtocol.Authentication;

namespace McpClientApp;

/// <summary>
/// Keeps the SDK's tokens in memory, as its default cache does, and writes to <paramref name="log"/> the scope of each
/// token the provider issues, never the token.
/// </summary>
public sealed class LoggedTokenCache(StepLog log) : ITokenCache
{
    private TokenContainer? _tokens;

    /// <summary>
    /// Counts the tokens stored, so a caller can tell whether a call obtained a new one.
    /// </summary>
    public int Issued { get; private set; }

    public ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
    {
        _tokens = tokens;
        Issued++;
        log.Add($"The provider issued an access token with scope {tokens.Scope}, expiring in {tokens.ExpiresIn} s" +
                (tokens.RefreshToken is null ? "" : ", and a refresh token"));
        return ValueTask.CompletedTask;
    }

    public ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(_tokens);
}
