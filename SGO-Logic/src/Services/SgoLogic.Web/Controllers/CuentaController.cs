using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public class CuentaController : Controller
{
    private readonly GatewayClient _gateway;

    public CuentaController(GatewayClient gateway)
    {
        _gateway = gateway;
    }

    [HttpGet]
    public IActionResult Perfil()
    {
        var userEmail = Request.Cookies["sgo_user_email"];
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Cuenta/Perfil" });
        }

        ViewBag.UserName = Request.Cookies["sgo_user_name"] ?? "Usuario MegaStore";
        ViewBag.UserEmail = userEmail;
        ViewBag.HasJwt = !string.IsNullOrWhiteSpace(Request.Cookies["sgo_jwt"]);

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Ordenes(int pagina = 1, int tamano = 10)
    {
        var userEmail = Request.Cookies["sgo_user_email"];
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Cuenta/Ordenes" });
        }

        ViewBag.UserEmail = userEmail;
        var historial = await _gateway.ObtenerHistorialClienteAsync(userEmail, pagina, tamano);
        return View(historial);
    }

    [HttpGet]
    public IActionResult Privacidad()
    {
        ViewBag.UserEmail = Request.Cookies["sgo_user_email"];
        return View();
    }
}
