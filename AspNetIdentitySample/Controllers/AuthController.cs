using System.Text.Json.Nodes;
using Abblix.Oidc.Server.Features.RandomGenerators;
using Abblix.Oidc.Server.Features.UserAuthentication;
using Abblix.Oidc.Server.Model;
using Abblix.Oidc.Server.Mvc;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Path = Abblix.Oidc.Server.Mvc.Path;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;
using UriBuilder = Abblix.Utils.UriBuilder;

namespace AspNetIdentitySample.Controllers;

public class AuthController : Controller
{
    // GET: Auth/Login
    public IActionResult Login([FromQuery(Name = "request_uri")] string requestUri)
    {
        // Return a view with login/password inputs and sign-in button
        return View(new { requestUri });
    }

    // POST: Auth/Login
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        [FromServices] IAuthSessionService authService,
        [FromServices] ISessionIdGenerator sessionIdGenerator,
        [FromServices] IUriResolver uriResolver,
        [FromServices] UserManager<IdentityUser> userManager,
        [FromServices] SignInManager<IdentityUser> signIn,
        [FromServices] TimeProvider clock,
        [FromForm] string email,
        [FromForm] string password,
        [FromForm] string requestUri)
    {
        var user = await userManager.FindByEmailAsync(email);

        // verifies the hash and enforces lockout, but issues no cookie: the session stays the library's
        var result = user is not null
            ? await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)
            : SignInResult.Failed;

        if (!result.Succeeded)
        {
            // Return an error message to the view to inform the user
            ModelState.AddModelError("", "Invalid username or password");
            return View(new { requestUri });
        }

        // If authentication is successful, create a new authentication session
        var authSession = new AuthSession(
            user!.Id,                                        // the subject the claims adapter resolves later
            sessionIdGenerator.GenerateSessionId(),
            clock.GetUtcNow(),
            CookieAuthenticationDefaults.AuthenticationScheme)
        {
            Email = user.Email,
            EmailVerified = user.EmailConfirmed,
            AuthenticationMethodReferences = ["pwd"],        // lands in the amr claim

            // snapshot the security stamp: the cookie's OnValidatePrincipal compares it on every
            // request, so a password change or UpdateSecurityStampAsync ends this session
            AdditionalClaims = new JsonObject
            {
                ["security_stamp"] = await userManager.GetSecurityStampAsync(user),
            },
        };

        // Sign in the user using the authentication service
        await authService.SignInAsync(authSession);

        // Redirect the user to the authorization endpoint URL, recovering the OIDC flow
        var authorizeUrl = new UriBuilder(uriResolver.Content(Path.Authorize))
            { Query = { [AuthorizationRequest.Parameters.RequestUri] = requestUri } };
        return Redirect(authorizeUrl);
    }
}
