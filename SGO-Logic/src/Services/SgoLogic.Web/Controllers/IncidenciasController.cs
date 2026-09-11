using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public class IncidenciasController : Controller
{
    private readonly GatewayClient _gateway;

    public IncidenciasController(GatewayClient gateway)
    {
        _gateway = gateway;
    }

    [HttpGet]
    public IActionResult Crear(string? ordenId)
    {
        ViewBag.OrdenId = ordenId;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Crear(string ordenId, string descripcion)
    {
        if (string.IsNullOrWhiteSpace(ordenId) || string.IsNullOrWhiteSpace(descripcion))
        {
            ViewBag.OrdenId = ordenId;
            ViewBag.Error = "Debe proporcionar el ID de la orden y una descripcion valida.";
            return View();
        }

        var (exito, incidencia, error) = await _gateway.CrearIncidenciaAsync(new CrearIncidenciaModel(ordenId.Trim(), descripcion.Trim()));
        if (!exito)
        {
            ViewBag.OrdenId = ordenId;
            ViewBag.Error = error ?? "Error al registrar la incidencia.";
            return View();
        }

        TempData["Exito"] = $"Incidencia registrada con éxito para la orden {ordenId}.";
        return RedirectToAction("Index", "Estado", new { ordenId = ordenId.Trim() });
    }
}
