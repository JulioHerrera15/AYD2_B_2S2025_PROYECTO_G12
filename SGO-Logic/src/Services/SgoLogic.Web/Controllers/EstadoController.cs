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

        ViewBag.OrdenId = ordenId.Trim();
        var orden = await _gateway.ObtenerEstadoOrdenAsync(ordenId.Trim());
        if (orden is null)
        {
            ViewBag.Error = $"No se encontró la orden con ID '{ordenId}' en el sistema.";
            return View();
        }

        var despacho = await _gateway.ObtenerDespachoOrdenAsync(ordenId.Trim());
        ViewBag.Despacho = despacho;

        return View(orden);
    }

    [HttpGet("Estado/Api/{ordenId}")]
    public async Task<IActionResult> Api(string ordenId)
    {
        if (string.IsNullOrWhiteSpace(ordenId))
            return BadRequest(new { Error = "ordenId es requerido" });

        var orden = await _gateway.ObtenerEstadoOrdenAsync(ordenId.Trim());
        if (orden is null)
            return NotFound(new { Error = "Orden no encontrada" });

        var despacho = await _gateway.ObtenerDespachoOrdenAsync(ordenId.Trim());

        return Json(new { orden, despacho });
    }
}
