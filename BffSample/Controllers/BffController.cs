using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace BffSample.Controllers;

[ApiController]
[Route("[controller]")]
public class BffController : Controller
{
    public const string CorsPolicyName = "Bff";

    [HttpGet("check_session")]
    [EnableCors(CorsPolicyName)]
    public ActionResult<IDictionary<string, string>> CheckSession()
    {
        // return 401 Unauthorized to force SPA redirection to Login endpoint
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        return User.Claims.ToDictionary(claim => claim.Type, claim => claim.Value);
    }

    [HttpGet("login")]
    public ActionResult<IDictionary<string, string>> Login()
    {
        // Logic to initiate the authorization code flow
        return Challenge(new AuthenticationProperties { RedirectUri = Url.Content("~/") });
    }

    // GET so the browser can navigate here: RP-initiated logout needs a top-level redirect to the
    // provider's end-session endpoint, which a fetch cannot perform. SameSite=Strict keeps a
    // cross-site GET from carrying the session cookie, so there is no CSRF logout.
    [HttpGet("logout")]
    public IActionResult Logout() => SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        CookieAuthenticationDefaults.AuthenticationScheme,
        OpenIdConnectDefaults.AuthenticationScheme);
}