using Microsoft.AspNetCore.Mvc;

namespace SgoLogic.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
