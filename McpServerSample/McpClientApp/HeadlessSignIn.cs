using System.Net;
using System.Web;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using ModelContextProtocol.Authentication;

namespace McpClientApp;

/// <summary>
/// Plays the user's browser without one: follows the provider's redirects to its login page, posts the form with the
/// demo user's credentials, answers the consent page with <paramref name="consent"/> (allow or deny), leaving checked
/// only the scopes in <paramref name="grantedScopes"/> when it is given, and reads the response from the redirect back to
/// <paramref name="redirectUri"/>, which it never requests. For running the flow unattended against the sample
/// provider, whose login form and consent page it knows.
/// </summary>
public sealed class HeadlessSignIn(
    Uri redirectUri,
    string email,
    string password,
    string consent,
    string[]? grantedScopes) : ISignIn
{
    private const int MaxRedirects = 10;

    public async Task<AuthorizationResult?> AuthorizeAsync(
        AuthorizationCallbackContext context,
        CancellationToken cancellationToken)
    {
        using var browser = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = new CookieContainer(),
        });

        var (loginPage, loginUri) = await FollowAsync(browser, context.AuthorizationUri, cancellationToken);
        var (consentPage, consentUri) = await SubmitAsync(
            browser, loginPage, loginUri, new() { ["email"] = email, ["password"] = password }, cancellationToken);

        // A refused sign-in (wrong credentials, an expired form) answers the login page again, which has no decision
        if (!IsAt(consentUri) && !await IsConsentPageAsync(consentPage, cancellationToken))
        {
            throw new InvalidOperationException(
                $"The sign-in did not lead to the consent page but to {consentUri}; check HeadlessSignIn:Email and " +
                "HeadlessSignIn:Password against the provider's demo user");
        }

        var redirect = consentUri;
        if (!IsAt(redirect))
        {
            (_, redirect) = await SubmitAsync(
                browser, consentPage, consentUri, new() { ["decision"] = consent }, cancellationToken);
        }

        if (!IsAt(redirect))
            throw new InvalidOperationException($"The consent page did not lead back to the redirect URI but to {redirect}");

        var query = HttpUtility.ParseQueryString(redirect.Query);
        if (query["error"] is { } error)
            throw new InvalidOperationException($"The provider answered {error}: {query["error_description"]}");

        return new AuthorizationResult { Code = query["code"], State = query["state"], Iss = query["iss"] };
    }

    /// <summary>
    /// Whether a browser would send the field: a checkbox only while checked, a scope checkbox only when
    /// <see cref="grantedScopes"/> names it or names nothing.
    /// </summary>
    private bool IsSent(IHtmlInputElement input)
        => input.Type != "checkbox" ||
           (input.IsChecked && (input.Name != "scope" || grantedScopes is null || grantedScopes.Contains(input.Value)));

    private bool IsAt(Uri uri) => uri.GetLeftPart(UriPartial.Path) == redirectUri.GetLeftPart(UriPartial.Path);

    private static async Task<bool> IsConsentPageAsync(string page, CancellationToken cancellationToken)
        => (await new HtmlParser().ParseDocumentAsync(page, cancellationToken)).QuerySelector("button[name=decision]") is not null;

    /// <summary>
    /// Posts the form on <paramref name="page"/> with its hidden fields and <paramref name="values"/>, and follows the
    /// answer as <see cref="FollowAsync"/> does.
    /// </summary>
    private async Task<(string Page, Uri Uri)> SubmitAsync(
        HttpClient browser,
        string page,
        Uri pageUri,
        Dictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        var form = (await new HtmlParser().ParseDocumentAsync(page, cancellationToken)).QuerySelector("form")
                       as IHtmlFormElement
                   ?? throw new InvalidOperationException($"No form at {pageUri}");

        // What a browser submits: every named field, a checkbox only while checked, and the user's own entries
        var fields = form.Elements.OfType<IHtmlInputElement>()
            .Where(input => !string.IsNullOrEmpty(input.Name) && !values.ContainsKey(input.Name!) && IsSent(input))
            .Select(input => KeyValuePair.Create(input.Name!, input.Value))
            .Concat(values)
            .ToList();

        var posted = await browser.PostAsync(
            new Uri(pageUri, form.GetAttribute("action")),
            new FormUrlEncodedContent(fields),
            cancellationToken);
        using (posted)
            return await FollowAsync(browser, pageUri, cancellationToken, posted);
    }

    /// <summary>
    /// Follows redirects from <paramref name="uri"/> until a page is answered, which is returned with its address, or
    /// until one points at the redirect URI, which is returned unrequested.
    /// </summary>
    private async Task<(string Page, Uri Uri)> FollowAsync(
        HttpClient browser,
        Uri uri,
        CancellationToken cancellationToken,
        HttpResponseMessage? first = null)
    {
        var response = first ?? await browser.GetAsync(uri, cancellationToken);
        for (var redirects = 0; redirects < MaxRedirects; redirects++)
        {
            try
            {
                if (response.StatusCode is not (HttpStatusCode.Found or HttpStatusCode.SeeOther or
                    HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.MovedPermanently))
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(
                            $"{uri} answered {(int)response.StatusCode}: {body}");
                    }

                    return (body, uri);
                }

                uri = new Uri(uri, response.Headers.Location
                                   ?? throw new InvalidOperationException($"{uri} redirected nowhere"));
                if (uri.GetLeftPart(UriPartial.Path) == redirectUri.GetLeftPart(UriPartial.Path))
                    return ("", uri);
            }
            finally
            {
                if (response != first)
                    response.Dispose();
            }

            response = await browser.GetAsync(uri, cancellationToken);
        }

        throw new InvalidOperationException($"More than {MaxRedirects} redirects from {uri}");
    }
}
