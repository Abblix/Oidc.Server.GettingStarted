using ModelContextProtocol.Authentication;

namespace McpClientApp;

/// <summary>
/// Holds a sign-in the SDK asks for until the user's browser returns from the provider to the redirect URI: the
/// assistant's page sends the browser to <see cref="PendingUri"/>, and <see cref="Receive"/> hands the SDK what the
/// provider sent back.
/// </summary>
public sealed class BrowserSignIn(StepLog log) : ISignIn
{
    private readonly Lock _lock = new();
    private TaskCompletionSource<AuthorizationResult>? _response;
    private TaskCompletionSource _requested = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// The authorization URL of the sign-in waiting for the browser, or none.
    /// </summary>
    public Uri? PendingUri { get; private set; }

    /// <summary>
    /// Completes when the SDK next asks for a sign-in, so a request that started a call can send the browser on while
    /// the call waits for it.
    /// </summary>
    public Task Requested
    {
        get
        {
            lock (_lock)
                return _requested.Task;
        }
    }

    public async Task<AuthorizationResult?> AuthorizeAsync(
        AuthorizationCallbackContext context,
        CancellationToken cancellationToken)
    {
        var response = new TaskCompletionSource<AuthorizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource requested;
        lock (_lock)
        {
            _response = response;
            PendingUri = context.AuthorizationUri;
            requested = _requested;
            _requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        log.Add($"Waiting for the browser to sign in at {context.AuthorizationUri}");
        requested.SetResult();
        try
        {
            return await response.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            lock (_lock)
            {
                if (_response == response)
                {
                    _response = null;
                    PendingUri = null;
                }
            }
        }
    }

    /// <summary>
    /// Hands the provider's redirect to the waiting sign-in: the code with state and iss, or the error. Returns false
    /// when no sign-in waits, as for a redirect opened again after its sign-in ended.
    /// </summary>
    public bool Receive(IQueryCollection query)
    {
        TaskCompletionSource<AuthorizationResult>? response;
        lock (_lock)
        {
            response = _response;
            _response = null;
            PendingUri = null;
        }

        if (response is null)
            return false;

        if (query["error"].ToString() is { Length: > 0 } error)
        {
            log.Add($"The provider redirected back with {error}: {query["error_description"]}");
            response.TrySetException(new InvalidOperationException($"The provider answered {error}: {query["error_description"]}"));
            return true;
        }

        log.Add("The provider redirected back with an authorization code");
        response.TrySetResult(new AuthorizationResult
        {
            Code = query["code"].ToString(),
            State = query["state"].ToString(),
            Iss = query["iss"].ToString() is { Length: > 0 } iss ? iss : null,
        });
        return true;
    }
}
