using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TestClientApp.Controllers;

public class HomeController : Controller
{
    [Authorize]
    public IActionResult Index()
    {
        // Retrieve and pass the user's claims to the view
        return View(User.Claims);
    }

    public IActionResult EndSession()
    {
        return SignOut(
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}
