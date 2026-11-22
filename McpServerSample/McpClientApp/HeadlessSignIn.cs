using System.Net;
using System.Web;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using ModelContextProtocol.Authentication;

namespace McpClientApp;

/// <summary>
/// Plays the user's browser without one: follows the provider's redirects to its login page, posts the form with the
/// demo user's credentials and reads the code from the redirect back to <paramref name="redirectUri"/>, which it never
/// requests. For running the flow unattended against the sample provider, whose login form it knows.
/// </summary>
public sealed class HeadlessSignIn(Uri redirectUri, string email, string password) : ISignIn
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
        var form = await new HtmlParser().ParseDocumentAsync(loginPage, cancellationToken) is var page &&
                   page.QuerySelector("form") is IHtmlFormElement found
            ? found
            : throw new InvalidOperationException($"No login form at {loginUri}");

        var fields = form.Elements.OfType<IHtmlInputElement>()
            .Where(input => !string.IsNullOrEmpty(input.Name))
            .ToDictionary(input => input.Name!, input => input.Value);
        fields["email"] = email;
        fields["password"] = password;

        using var posted = await browser.PostAsync(
            new Uri(loginUri, form.GetAttribute("action")),
            new FormUrlEncodedContent(fields),
            cancellationToken);
        var (answer, redirect) = await FollowAsync(browser, loginUri, cancellationToken, posted);

        // A refused sign-in (wrong credentials, an expired form) answers the login page again rather than redirecting
        if (redirect.GetLeftPart(UriPartial.Path) != redirectUri.GetLeftPart(UriPartial.Path))
            throw new InvalidOperationException($"The sign-in did not lead back to the redirect URI but to {redirect}: {answer}");

        var query = HttpUtility.ParseQueryString(redirect.Query);
        if (query["error"] is { } error)
            throw new InvalidOperationException($"The provider answered {error}: {query["error_description"]}");

        return new AuthorizationResult { Code = query["code"], State = query["state"], Iss = query["iss"] };
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
