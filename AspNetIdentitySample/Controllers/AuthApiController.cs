using System.Security.Claims;
using System.Text.Json.Nodes;
using Abblix.Oidc.Server.Features.RandomGenerators;
using Abblix.Oidc.Server.Features.UserAuthentication;
using Abblix.Oidc.Server.Model;
using Abblix.Oidc.Server.Mvc;
using AspNetIdentitySample.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Path = Abblix.Oidc.Server.Mvc.Path;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;
using UriBuilder = Abblix.Utils.UriBuilder;

namespace AspNetIdentitySample.Controllers;

// The JSON contract the React auth SPA talks to. Both endpoints end the same way a server-rendered
// form did: on success they establish the library session and return the authorize URL that resumes
// the OIDC flow, which the SPA then navigates to. Validation failures come back as a standard
// ValidationProblem so the SPA can show them inline.
[ApiController]
[Route("api/auth")]
public sealed class AuthApiController : ControllerBase
{
    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    [ProducesResponseType<AuthSuccessResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Login(
        [FromServices] IAuthSessionService authService,
        [FromServices] ISessionIdGenerator sessionIdGenerator,
        [FromServices] IUriResolver uriResolver,
        [FromServices] UserManager<IdentityUser> userManager,
        [FromServices] SignInManager<IdentityUser> signIn,
        [FromServices] TimeProvider clock,
        [FromBody] LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);

        // Verifies the hash and enforces lockout, but issues no cookie: the session stays the library's.
        var result = user is not null
            ? await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)
            : SignInResult.Failed;

        if (!result.Succeeded)
        {
            ModelState.AddModelError(nameof(request.Password), "Invalid email or password.");
            return ValidationProblem(ModelState);
        }

        var redirectUrl = await SignInAndBuildResumeUrl(
            authService, sessionIdGenerator, uriResolver, userManager, clock, user!, request.RequestUri);
        return Ok(new AuthSuccessResponse { RedirectUrl = redirectUrl });
    }

    [HttpPost("register")]
    [ValidateAntiForgeryToken]
    [ProducesResponseType<AuthSuccessResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        [FromServices] IAuthSessionService authService,
        [FromServices] ISessionIdGenerator sessionIdGenerator,
        [FromServices] IUriResolver uriResolver,
        [FromServices] UserManager<IdentityUser> userManager,
        [FromServices] TimeProvider clock,
        [FromBody] RegisterRequest request)
    {
#warning DEV shortcut: sign-up leaves the address unconfirmed, so email_verified is honestly false, yet still lets the account sign in. A real deployment emails a confirmation link and only trusts the address once the user proves control of the mailbox.
        var user = new IdentityUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = false,
        };

        // CreateAsync enforces the password policy configured in Program.cs and stores only a salted
        // PBKDF2 hash, never the plaintext.
        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            // Surface Identity's own messages (password too short, email already taken, ...) inline.
            foreach (var error in created.Errors)
                ModelState.AddModelError(nameof(request.Password), error.Description);
            return ValidationProblem(ModelState);
        }

        // profile claims Identity does not model live in its claim store; the IdentityUserInfoProvider
        // surfaces them when the profile scope asks.
        await userManager.AddClaimAsync(user, new Claim("name", request.Name));

        var redirectUrl = await SignInAndBuildResumeUrl(
            authService, sessionIdGenerator, uriResolver, userManager, clock, user, request.RequestUri);
        return Ok(new AuthSuccessResponse { RedirectUrl = redirectUrl });
    }

    // Login and sign-up share their tail: establish the library session, then return the authorize URL
    // that resumes the OIDC flow the user was pulled out of.
    private static async Task<string> SignInAndBuildResumeUrl(
        IAuthSessionService authService,
        ISessionIdGenerator sessionIdGenerator,
        IUriResolver uriResolver,
        UserManager<IdentityUser> userManager,
        TimeProvider clock,
        IdentityUser user,
        string? requestUri)
    {
        var authSession = new AuthSession(
            user.Id,
            sessionIdGenerator.GenerateSessionId(),
            clock.GetUtcNow(),
            CookieAuthenticationDefaults.AuthenticationScheme)
        {
            Email = user.Email,
            EmailVerified = user.EmailConfirmed,
            AuthenticationMethodReferences = ["pwd"],

            // snapshot the security stamp: the cookie's OnValidatePrincipal compares it on every
            // request, so a password change or UpdateSecurityStampAsync ends this session
            AdditionalClaims = new JsonObject
            {
                ["security_stamp"] = await userManager.GetSecurityStampAsync(user),
            },
        };

        await authService.SignInAsync(authSession);

        var authorizeUrl = new UriBuilder(uriResolver.Content(Path.Authorize))
            { Query = { [AuthorizationRequest.Parameters.RequestUri] = requestUri } };
        return authorizeUrl;
    }
}
