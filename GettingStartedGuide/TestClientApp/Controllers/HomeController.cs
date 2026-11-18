using System.Diagnostics;
// #region usings-logout
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
// #endregion
// #region using-authorization
using Microsoft.AspNetCore.Authorization;
// #endregion
using Microsoft.AspNetCore.Mvc;
using TestClientApp.Models;

namespace TestClientApp.Controllers;

public class HomeController : Controller
{
    // #region authorize
    [Authorize]
    // #endregion
    // #region index
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

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
