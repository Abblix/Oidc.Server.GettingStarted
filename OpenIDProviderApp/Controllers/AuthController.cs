using Abblix.Oidc.Server.Common.Configuration;
using Abblix.Oidc.Server.Common.Interfaces;
using Abblix.Oidc.Server.Features.RandomGenerators;
using Abblix.Oidc.Server.Features.UserAuthentication;
using Abblix.Oidc.Server.Model;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using UriBuilder = Abblix.Utils.UriBuilder;

namespace OpenIDProviderApp.Controllers;

public class AuthController: Controller
{
    // GET: Auth/Login
    public IActionResult Login([FromQuery(Name = "request_uri")] string requestUri)
    {
        // Return a view with login/password inputs and sign-in button
        return View(new { requestUri });
    }

    // POST: Auth/Login
    [HttpPost]
    public async Task<IActionResult> Login(
        [FromServices] IAuthSessionService authService,
        [FromServices] ISessionIdGenerator sessionIdGenerator,
        [FromServices] IOidcEndpointResolver endpointResolver,
        [FromServices] TestUserStorage userStorage,
        [FromForm] string email,
        [FromForm] string password,
        [FromForm] string requestUri)
    {
        // Attempt to authenticate the user with provided credentials
        if (!userStorage.TryAuthenticate(email, password, out var subject))
        {
            // Return an error message to the view to inform the user
            ModelState.AddModelError("", "Invalid username or password");
            return View(new { requestUri });
        }

        // If authentication is successful, create a new authentication session
        var authSession = new AuthSession(
            subject,
            sessionIdGenerator.GenerateSessionId(),
            DateTimeOffset.UtcNow,
            CookieAuthenticationDefaults.AuthenticationScheme);

        // Sign in the user using the authentication service
        await authService.SignInAsync(authSession);

        // Redirect the user to the authorization endpoint URL, recovering the OIDC flow. Every endpoint is
        // enabled here, as the defaults leave them, so the resolver always has an address to return.
        var authorizeUrl = new UriBuilder(endpointResolver.Resolve(OidcEndpoints.Authorize)!)
            { Query = { [AuthorizationRequest.Parameters.RequestUri] = requestUri } };
        return Redirect(authorizeUrl);
    }
}
