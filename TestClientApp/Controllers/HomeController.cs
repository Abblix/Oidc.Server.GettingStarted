using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

namespace TestClientApp.Controllers;

public class HomeController(IHttpClientFactory httpClientFactory, IConfiguration configuration) : Controller
{
    // The API the access token was requested for is the API this page calls
    private const string ResourceKey = "OpenIdConnect:Resource";

    [Authorize]
    public IActionResult Index()
    {
        // Retrieve and pass the user's claims to the view
        return View(User.Claims);
    }

    [Authorize]
    public async Task<IActionResult> Weather()
    {
        var apiAddress = configuration[ResourceKey]
                         ?? throw new InvalidOperationException($"The value {ResourceKey} must be set");

        // SaveTokens keeps the tokens from the sign-in in the session cookie
        var accessToken = await HttpContext.GetTokenAsync("access_token");

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(apiAddress), "weatherforecast"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request);

        // The API's own status is passed on, so a refused token shows as a refusal rather than a page
        return new ContentResult
        {
            StatusCode = (int)response.StatusCode,
            Content = await response.Content.ReadAsStringAsync(),
            ContentType = response.Content.Headers.ContentType?.ToString(),
        };
    }

    // Signing out changes state, so it takes a form post with an antiforgery token. As a GET, a link or a redirect
    // on any other site would sign the user out of this client and of the provider behind it, because the browser
    // sends the session cookie along on such a top-level navigation.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult EndSession()
    {
        return SignOut(
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}
