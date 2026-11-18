// #region usings-logout
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
// #endregion
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TestClientApp.Controllers;

public class HomeController : Controller
{
    // #region index
    [Authorize]
    public IActionResult Index()
    {
        // Retrieve and pass the user's claims to the view
        return View(User.Claims);
    }
    // #endregion

    // #region end-session
    public IActionResult EndSession()
    {
        // #region sign-out
        return SignOut(
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
        // #endregion
    }
    // #endregion
}
