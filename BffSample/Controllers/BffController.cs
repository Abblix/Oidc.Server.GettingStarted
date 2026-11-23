using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

namespace BffSample.Controllers;

[ApiController]
[Route("[controller]")]
public class BffController : Controller
{
    [HttpGet("check_session")]
    public ActionResult<IDictionary<string, object>> CheckSession()
    {
        // return 401 Unauthorized to force SPA redirection to Login endpoint
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        // A JSON array in the ID token (amr, or a multi-valued custom claim) arrives as one claim per element, so
        // claims are grouped by type: a single value comes back as a string, several as an array. An array of one
        // element therefore reads as a string.
        return User.Claims
            .GroupBy(claim => claim.Type)
            .ToDictionary(
                group => group.Key,
                group => group.Count() == 1 ? (object)group.Single().Value : group.Select(claim => claim.Value).ToArray());
    }

    // The SPA navigates here, since the authorization request is a top-level redirect to the provider. A page on
    // another site sending the browser here only starts a sign-in the user sees.
    [HttpGet("login")]
    [BrowserNavigation]
    public ActionResult<IDictionary<string, string>> Login()
    {
        // Logic to initiate the authorization code flow
        return Challenge(new AuthenticationProperties { RedirectUri = Url.Content("~/") });
    }

    // A POST with the CSRF header like every other call, so a page elsewhere, a sibling on the same site included,
    // cannot end the session. It answers with the address of LogoutContinue and a ticket (see Program.cs), which the
    // SPA then navigates to for the RP-initiated logout.
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        // The cookie is signed out without the address to return to, which would make its handler answer with a
        // redirect, and the fetch would follow it instead of reading the end-session address
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return SignOut(new AuthenticationProperties { RedirectUri = "/" }, OpenIdConnectDefaults.AuthenticationScheme);
    }

    // The browser navigates here with the ticket the logout POST answered, and is sent on to the provider's
    // end-session endpoint. The session is already gone and the ticket works once, so a page elsewhere sending the
    // browser here can only end a logout the user started, and only with a ticket it cannot read.
    [HttpGet("logout/continue")]
    [BrowserNavigation]
    public IActionResult LogoutContinue([FromQuery] string ticket, [FromServices] LogoutTickets tickets)
        => tickets.Redeem(ticket) is { } endSessionUrl ? Redirect(endSessionUrl) : BadRequest();
}
