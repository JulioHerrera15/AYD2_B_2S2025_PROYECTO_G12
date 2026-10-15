using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public class NotificacionesController : Controller
{
    private readonly GatewayClient _gateway;

    public NotificacionesController(GatewayClient gateway)
    {
        _gateway = gateway;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userRole = Request.Cookies["sgo_user_role"] ?? "CLIENTE";
        var userEmail = Request.Cookies["sgo_user_email"];
        var jwt = Request.Cookies["sgo_jwt"];

        if (!PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt))
        {
            TempData["Error"] = "Acceso restringido: Se requieren privilegios de personal administrativo o soporte.";
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Notificaciones" });
        }

        var ct = HttpContext.RequestAborted;
        var microkernelInfo = await _gateway.ObtenerPluginsKernelAsync(ct);
        var pendientes = await _gateway.ObtenerPendientesKernelAsync(ct);
        var mensajes = await _gateway.ObtenerMensajesAsync(ct: ct);

        ViewBag.MicrokernelInfo = microkernelInfo;
        ViewBag.Pendientes = pendientes;
        ViewBag.Mensajes = mensajes;
        ViewBag.UserEmail = userEmail;
        ViewBag.UserRole = userRole;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePlugin(string id, bool habilitado)
    {
        var userRole = Request.Cookies["sgo_user_role"] ?? "CLIENTE";
        var userEmail = Request.Cookies["sgo_user_email"];
        var jwt = Request.Cookies["sgo_jwt"];

        if (!PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            TempData["Error"] = "Identificador de plugin invalido.";
            return RedirectToAction(nameof(Index));
        }

        var (exito, error) = await _gateway.TogglePluginKernelAsync(id, habilitado, HttpContext.RequestAborted);
        if (exito)
        {
            TempData["Exito"] = $"Plugin '{id}' {(habilitado ? "habilitado" : "deshabilitado")} exitosamente en el Microkernel.";
        }
        else
        {
            TempData["Error"] = $"No se pudo cambiar el estado del plugin '{id}': {error}";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Despachar(string destinatario, string canal, string asunto, string cuerpo)
    {
        var userRole = Request.Cookies["sgo_user_role"] ?? "CLIENTE";
        var userEmail = Request.Cookies["sgo_user_email"];
        var jwt = Request.Cookies["sgo_jwt"];

        if (!PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(destinatario) || string.IsNullOrWhiteSpace(cuerpo))
        {
            TempData["Error"] = "Destinatario y cuerpo son obligatorios para el despacho.";
            return RedirectToAction(nameof(Index));
        }

        var (exito, error, resultado) = await _gateway.DespacharKernelAsync(
            destinatario.Trim(),
            asunto?.Trim() ?? "Notificacion de Prueba Microkernel",
            cuerpo.Trim(),
            canal?.Trim().ToUpperInvariant() ?? "EMAIL",
            HttpContext.RequestAborted);

        if (exito && resultado is not null)
        {
            if (resultado.Exitoso)
            {
                TempData["Exito"] = $"Notificacion despachada con exito via plugin '{resultado.PluginUtilizado}'.";
            }
            else
            {
                TempData["Alerta"] = $"El despacho no se completo directamente, pero el Microkernel aislo la falla: {resultado.Error}";
            }
        }
        else
        {
            TempData["Error"] = $"Fallo al invocar el Microkernel: {error}";
        }

        return RedirectToAction(nameof(Index));
    }
}
