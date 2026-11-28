using System.Net;
using System.Net.Http.Headers;

namespace McpClientApp;

/// <summary>
/// Sits under the SDK's HTTP client, which also fetches the metadata documents, and writes to <paramref name="log"/>
/// what the browser run shows of the protocol: each challenge with its <c>WWW-Authenticate</c> and each metadata
/// document read. It never writes a request's <c>Authorization</c> header or a response body, so no token reaches the log.
/// </summary>
public sealed class TrackerTraffic(StepLog log) : DelegatingHandler(new HttpClientHandler())
{
    private const string WellKnownPath = "/.well-known/";

    /// <summary>
    /// The scope named by the latest <c>403 insufficient_scope</c>, or none since <see cref="ForgetRefusal"/>.
    /// </summary>
    public string? RefusedScope { get; private set; }

    public void ForgetRefusal() => RefusedScope = null;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        var uri = request.RequestUri;
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                var challenge = string.Join(", ", response.Headers.WwwAuthenticate);
                log.Add($"{request.Method} {uri} answered {(int)response.StatusCode}: WWW-Authenticate: {challenge}");
                if (response.StatusCode == HttpStatusCode.Forbidden &&
                    Parameter(response.Headers.WwwAuthenticate, "error") == "insufficient_scope")
                {
                    RefusedScope = Parameter(response.Headers.WwwAuthenticate, "scope");
                }

                break;

            case var _ when uri?.AbsolutePath.StartsWith(WellKnownPath, StringComparison.Ordinal) == true:
                log.Add($"Read {uri} ({(int)response.StatusCode})");
                break;
        }

        return response;
    }

    /// <summary>
    /// A parameter of the Bearer challenge, such as <c>error="insufficient_scope"</c> (RFC 6750 section 3).
    /// </summary>
    private static string? Parameter(HttpHeaderValueCollection<AuthenticationHeaderValue> challenges, string name)
        => challenges
            .Where(challenge => challenge.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
            .SelectMany(challenge => (challenge.Parameter ?? "").Split(','))
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0].Trim() == name)
            .Select(pair => pair[1].Trim().Trim('"'))
            .FirstOrDefault();
}
