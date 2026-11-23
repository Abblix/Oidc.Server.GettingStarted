namespace BffSample;

/// <summary>
/// The static header the SPA adds to every request it sends the BFF, as RFC 10017 section 6.1.3.3.2 describes.
/// Its value is no secret: what protects the session is that a page on another origin cannot send any custom
/// header to the BFF without the BFF approving it in a CORS preflight, and this BFF approves none.
/// </summary>
/// <remarks>
/// ClientApp/src/components/Bff.tsx sends the same name and value.
/// </remarks>
public static class CsrfHeader
{
    public const string Name = "X-CSRF";
    public const string Value = "1";

    /// <summary>
    /// Whether <paramref name="request"/> carries the header with its value, once.
    /// </summary>
    public static bool IsPresent(HttpRequest request)
        => request.Headers.TryGetValue(Name, out var values) && values is [Value];
}
