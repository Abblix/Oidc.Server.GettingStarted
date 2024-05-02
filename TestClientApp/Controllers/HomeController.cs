using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

public class HomeController : Controller
{
    [Authorize]
    public IActionResult Index()
    {
        // Retrieve and pass the user's claims to the view
        return View(User.Claims);
    }
}