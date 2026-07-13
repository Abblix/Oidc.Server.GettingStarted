using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace AspNetIdentitySample.Controllers;

// Serves the React auth SPA. The OIDC library redirects unauthenticated users to LoginUri
// (/Auth/Login), and the "create account" link inside the SPA client-routes to /Auth/Register; both
// paths return the same single-page app, which reads request_uri from the query string and drives
// sign-in or sign-up against the JSON endpoints in AuthApiController.
public sealed class AuthController(IAntiforgery antiforgery, IWebHostEnvironment environment) : Controller
{
    [HttpGet("/Auth/Login")]
    [HttpGet("/Auth/Register")]
    public IActionResult Index()
    {
        // Issue the antiforgery request token in a JS-readable cookie. The SPA echoes it back in the
        // X-CSRF-TOKEN header on every POST, which the [ValidateAntiForgeryToken] endpoints validate
        // against the paired HttpOnly cookie. A server-rendered form did this with a hidden field; a
        // SPA has to perform the same handshake explicitly.
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });

        // The SPA is built into wwwroot/auth by the ClientApp Vite project; request_uri stays in the
        // URL and is read by the SPA, so nothing needs to be injected into the page here.
        var indexHtml = Path.Combine(environment.WebRootPath, "auth", "index.html");
        return PhysicalFile(indexHtml, "text/html");
    }
}
