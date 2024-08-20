// Abblix OIDC Server Library
// Copyright (c) Abblix LLP. All rights reserved.
//
// DISCLAIMER: This software is provided 'as-is', without any express or implied
// warranty. Use at your own risk. Abblix LLP is not liable for any damages
// arising from the use of this software.
//
// LICENSE RESTRICTIONS: This code may not be modified, copied, or redistributed
// in any form outside of the official GitHub repository at:
// https://github.com/Abblix/OIDC.Server. All development and modifications
// must occur within the official repository and are managed solely by Abblix LLP.
//
// Unauthorized use, modification, or distribution of this software is strictly
// prohibited and may be subject to legal action.
//
// For full licensing terms, please visit:
//
// https://oidc.abblix.com/license
//
// CONTACT: For license inquiries or permissions, contact Abblix LLP at
// info@abblix.com

using System.Web;
using Abblix.Oidc.Server.Features.RandomGenerators;
using Abblix.Oidc.Server.Features.UserAuthentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

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
        return Redirect($"{Abblix.Oidc.Server.Mvc.Path.Authorize}?request_uri={HttpUtility.UrlEncode(requestUri)}");
    }
}
