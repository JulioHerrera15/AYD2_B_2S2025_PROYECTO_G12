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
    public async Task<IActionResult> Index(string? ordenId, string? estado)
    {
        ViewBag.OrdenId = ordenId;
        ViewBag.Estado = estado ?? "TODOS";

        var filtroEstado = string.Equals(estado, "TODOS", StringComparison.OrdinalIgnoreCase) ? null : estado;
        var incidencias = await _gateway.ListarIncidenciasAsync(ordenId, filtroEstado);

        return View(incidencias ?? new List<IncidenciaModel>());
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
            ViewBag.Error = "Debe proporcionar el ID de la orden y una descripción válida.";
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

    [HttpPost]
    public async Task<IActionResult> Resolver(Guid id, string resolucion, string? nuevoEstado)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(resolucion))
        {
            TempData["Error"] = "Debe proporcionar una resolución válida.";
            return RedirectToAction("Index");
        }

        var estadoFinal = string.IsNullOrWhiteSpace(nuevoEstado) ? "RESUELTA" : nuevoEstado.Trim();
        var (exito, error) = await _gateway.ActualizarIncidenciaAsync(id, null, estadoFinal, resolucion.Trim());

        if (!exito)
        {
            TempData["Error"] = $"Error al resolver incidencia: {error}";
        }
        else
        {
            TempData["Exito"] = $"Incidencia {id} actualizada a {estadoFinal} exitosamente.";
        }

        return RedirectToAction("Index");
    }
}
