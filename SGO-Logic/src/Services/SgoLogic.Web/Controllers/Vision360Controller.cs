using System.Diagnostics;
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

        var userRole = Request.Cookies["sgo_user_role"];
        var userEmail = Request.Cookies["sgo_user_email"];
        if (!PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt))
        {
            TempData["Error"] = "Acceso denegado. La Visión 360° es exclusiva para agentes de atención y administradores de MegaStore.";
            return RedirectToAction("Index", "Home");
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
            var sw = Stopwatch.StartNew();

            // Consultar datos reales de identidad y rol en Auth.Api
            var infoUsuario = await _gateway.ObtenerUsuarioPorEmailAsync(targetCliente);

            // Consultar historial real de órdenes en ConsultaEstado
            var historial = await _gateway.ObtenerHistorialClienteAsync(targetCliente);

            // Consultar carrito activo real en Redis
            var carrito = await _gateway.ObtenerCarritoPorIdAsync(targetCliente);

            // Consultar incidencias reales registradas
            var todasIncidencias = await _gateway.ListarIncidenciasAsync();
            var ordenIds = historial?.Ordenes?.Select(o => o.OrdenId).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();
            var incidenciasCliente = todasIncidencias?.Where(i => ordenIds.Contains(i.OrdenId)).ToList() ?? new List<IncidenciaModel>();

            sw.Stop();

            ViewBag.InfoUsuario = infoUsuario;
            ViewBag.Historial = historial;
            ViewBag.Carrito = carrito;
            ViewBag.Incidencias = incidenciasCliente;
            ViewBag.LatenciaMs = Math.Max(sw.ElapsedMilliseconds, 1);
        }

        return View();
    }

    [HttpPost]
    public async Task<IActionResult> AsignarRol(string email, string nuevoRol)
    {
        var jwt = Request.Cookies["sgo_jwt"];
        var userRole = Request.Cookies["sgo_user_role"];
        var userEmail = Request.Cookies["sgo_user_email"];
        if (!PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            TempData["Error"] = "El correo del usuario es requerido.";
            return RedirectToAction("Index");
        }

        var rolLimpio = string.IsNullOrWhiteSpace(nuevoRol) ? "CLIENTE" : nuevoRol.Trim().ToUpperInvariant();
        if (rolLimpio != "CLIENTE" && rolLimpio != "OPERADOR" && rolLimpio != "ADMIN")
        {
            TempData["Error"] = $"El rol '{nuevoRol}' no es válido. Roles oficiales permitidos: CLIENTE, OPERADOR, ADMIN.";
            return RedirectToAction("Index", new { email });
        }

        var (exito, error) = await _gateway.AsignarRolAsync(email.Trim(), rolLimpio);
        if (!exito)
        {
            TempData["Error"] = error ?? "No se pudo actualizar el rol del usuario.";
        }
        else
        {
            TempData["Exito"] = $"Rol oficial actualizado exitosamente a '{rolLimpio}' para el usuario {email.Trim()}.";
        }

        return RedirectToAction("Index", new { email = email.Trim() });
    }
}

