using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public class Vision360Controller : Controller
{
    private readonly GatewayClient _gateway;

    public Vision360Controller(GatewayClient gateway)
    {
        _gateway = gateway;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? email, string? clienteId)
    {
        var jwt = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(jwt))
        {
            TempData["Error"] = "Acceso restringido. Inicia sesión con tus credenciales de agente de atención para consultar la Visión 360.";
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Vision360" });
        }

        string? targetCliente = null;

        if (!string.IsNullOrWhiteSpace(clienteId))
        {
            targetCliente = clienteId.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(email))
        {
            targetCliente = email.Trim();
        }
        else
        {
            // Tomar el correo de la sesión activa como punto de partida si existe
            targetCliente = Request.Cookies["sgo_user_email"];
        }

        ViewBag.TargetCliente = targetCliente;

        if (!string.IsNullOrWhiteSpace(targetCliente))
        {
            // Consultar historial real de órdenes en ConsultaEstado
            var historial = await _gateway.ObtenerHistorialClienteAsync(targetCliente);

            // Consultar carrito activo real en Redis
            var carrito = await _gateway.ObtenerCarritoPorIdAsync(targetCliente);

            // Consultar incidencias reales registradas
            var todasIncidencias = await _gateway.ListarIncidenciasAsync();
            var ordenIds = historial?.Ordenes?.Select(o => o.OrdenId).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();
            var incidenciasCliente = todasIncidencias?.Where(i => ordenIds.Contains(i.OrdenId)).ToList() ?? new List<IncidenciaModel>();

            ViewBag.Historial = historial;
            ViewBag.Carrito = carrito;
            ViewBag.Incidencias = incidenciasCliente;
        }

        return View();
    }
}
