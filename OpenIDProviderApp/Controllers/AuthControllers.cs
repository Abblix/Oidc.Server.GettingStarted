using Abblix.Oidc.Server.Features.RandomGenerators;
using Abblix.Oidc.Server.Features.UserAuthentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Web;

namespace OpenIDProviderApp.Controllers
{
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
        public async Task<IActionResult> Login(
            [FromServices] IAuthSessionService authService,
            [FromServices] ISessionIdGenerator sessionIdGenerator,
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

            // Redirect the user to the authorization endpoint URL, recovering the OIDC flow
            return Redirect($"/connect/authorize?request_uri={HttpUtility.UrlEncode(requestUri)}");
        }
    }
}
