using System.Net;
using ModelContextProtocol.Authentication;

namespace McpClientApp;

/// <summary>
/// Prints the authorization URL for the user to open in a browser and waits for the provider's redirect, which this
/// app receives on its loopback redirect URI.
/// </summary>
public sealed class BrowserSignIn(string clientName, Uri redirectUri) : ISignIn
{
    // One per sign-in: the SDK signs in again when the tracker asks for a scope the token lacks, and that sign-in waits
    // for a redirect of its own, not the first one's
    private TaskCompletionSource<AuthorizationResult> _response = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<AuthorizationResult?> AuthorizeAsync(
        AuthorizationCallbackContext context,
        CancellationToken cancellationToken)
    {
        var response = new TaskCompletionSource<AuthorizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _response = response;
        Console.WriteLine("Open this address in a browser and sign in:");
        Console.WriteLine(context.AuthorizationUri);
        Console.WriteLine("An error page there instead of the login page means the provider refused the request, and " +
                          "its log says why; the provider does not redirect back after that, so stop this app with Ctrl+C.");
        return await response.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// The endpoint at <see cref="redirectUri"/>: hands the response to the waiting authorization and tells the user
    /// to go back to the terminal.
    /// </summary>
    public IResult Receive(HttpRequest request)
    {
        var query = request.Query;
        if (query["error"].ToString() is { Length: > 0 } error)
        {
            _response.TrySetException(new InvalidOperationException(
                $"The provider answered {error}: {query["error_description"]}"));
            return Page($"The provider refused the request: {error}");
        }

        _response.TrySetResult(new AuthorizationResult
        {
            Code = query["code"].ToString(),
            State = query["state"].ToString(),
            Iss = query["iss"].ToString() is { Length: > 0 } iss ? iss : null,
        });
        return Page("Signed in. The assistant goes on in the terminal; this tab can be closed.");
    }

    private IResult Page(string message)
        => Results.Content(
            $"""
             <!DOCTYPE html>
             <html>
             <head><title>{WebUtility.HtmlEncode(clientName)}</title></head>
             <body>
                 <h1>{WebUtility.HtmlEncode(clientName)}</h1>
                 <p>{WebUtility.HtmlEncode(message)}</p>
                 <p>Redirect URI: {WebUtility.HtmlEncode(redirectUri.ToString())}</p>
             </body>
             </html>
             """,
            "text/html");
}
