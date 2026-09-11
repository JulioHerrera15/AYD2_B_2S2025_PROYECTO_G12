using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public class EstadoController : Controller
{
    private readonly GatewayClient _gateway;

    public EstadoController(GatewayClient gateway)
    {
        _gateway = gateway;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? ordenId)
    {
        if (string.IsNullOrWhiteSpace(ordenId))
        {
            return View();
        }

        ViewBag.OrdenId = ordenId;
        var orden = await _gateway.ObtenerEstadoOrdenAsync(ordenId.Trim());
        if (orden is null)
        {
            ViewBag.Error = $"No se encontró la orden con ID '{ordenId}' en el sistema.";
        }

        return View(orden);
    }
}
