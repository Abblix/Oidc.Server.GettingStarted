using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TestClientApp.Controllers;

[Authorize]
public class HomeController : Controller
{
    public IActionResult Index()
    {
        // Retrieve and pass the user's claims to the view
        return View(User.Claims);
    }

    public IActionResult EndSession()
    {
        return SignOut();
    }
}
