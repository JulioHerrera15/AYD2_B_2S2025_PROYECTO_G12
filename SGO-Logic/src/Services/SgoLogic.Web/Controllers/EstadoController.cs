using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public sealed record OrdenConDespachoItem(
    OrdenHistorialResumenModel Orden,
    DespachoOrdenModel? Despacho);

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
        var jwt = Request.Cookies["sgo_jwt"];
        var userEmail = Request.Cookies["sgo_user_email"];
        var userRole = Request.Cookies["sgo_user_role"] ?? "CLIENTE";
        var isAuthenticated = !string.IsNullOrWhiteSpace(jwt) && !string.IsNullOrWhiteSpace(userEmail);

        ViewBag.IsAuthenticated = isAuthenticated;
        ViewBag.UserEmail = userEmail;

        // 1. Si no esta autenticado
        if (!isAuthenticated)
        {
            if (!string.IsNullOrWhiteSpace(ordenId))
            {
                TempData["Info"] = "Por motivos de privacidad y proteccion de datos, inicia sesion para rastrear tu orden.";
                return RedirectToAction("Login", "Auth", new { returnUrl = $"/Estado?ordenId={ordenId.Trim()}" });
            }

            // Muestra pantalla informativa segura sin buscador arbitrario
            return View();
        }

        // 2. Si esta autenticado pero no paso ordenId: listar sus propias guias y pedidos
        if (string.IsNullOrWhiteSpace(ordenId))
        {
            await CargarHistorialUsuarioAsync(userEmail!);
            return View();
        }

        // 3. Si paso ordenId: cargar detalle con verificacion estricta de pertenencia
        var idLimpio = ordenId.Trim();
        ViewBag.OrdenId = idLimpio;

        var orden = await _gateway.ObtenerEstadoOrdenAsync(idLimpio);
        if (orden is null)
        {
            ViewBag.Error = $"No se encontro la orden '{idLimpio}' en el sistema.";
            await CargarHistorialUsuarioAsync(userEmail!);
            return View();
        }

        var esDuenio = string.Equals(orden.ClienteId, userEmail, StringComparison.OrdinalIgnoreCase);
        var esPersonal = PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt);

        if (!esDuenio && !esPersonal)
        {
            ViewBag.Error = "Acceso denegado: Por razones de privacidad, solo puedes consultar las ordenes y guias asociadas a tu cuenta.";
            await CargarHistorialUsuarioAsync(userEmail!);
            return View();
        }

        var despacho = await _gateway.ObtenerDespachoOrdenAsync(idLimpio);
        ViewBag.Despacho = despacho;

        return View(orden);
    }

    [HttpGet("Estado/Api/{ordenId}")]
    public async Task<IActionResult> Api(string ordenId)
    {
        if (string.IsNullOrWhiteSpace(ordenId))
            return BadRequest(new { Error = "ordenId es requerido" });

        var jwt = Request.Cookies["sgo_jwt"];
        var userEmail = Request.Cookies["sgo_user_email"];
        var userRole = Request.Cookies["sgo_user_role"] ?? "CLIENTE";
        var isAuthenticated = !string.IsNullOrWhiteSpace(jwt) && !string.IsNullOrWhiteSpace(userEmail);

        if (!isAuthenticated)
            return Unauthorized(new { Error = "Requiere autenticacion para consultar estado" });

        var idLimpio = ordenId.Trim();
        var orden = await _gateway.ObtenerEstadoOrdenAsync(idLimpio);
        if (orden is null)
            return NotFound(new { Error = "Orden no encontrada" });

        var esDuenio = string.Equals(orden.ClienteId, userEmail, StringComparison.OrdinalIgnoreCase);
        var esPersonal = PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt);

        if (!esDuenio && !esPersonal)
            return StatusCode(StatusCodes.Status403Forbidden, new { Error = "Acceso denegado a orden de otro usuario" });

        var despacho = await _gateway.ObtenerDespachoOrdenAsync(idLimpio);

        return Json(new { orden, despacho });
    }

    private async Task CargarHistorialUsuarioAsync(string email)
    {
        var historial = await _gateway.ObtenerHistorialClienteAsync(email);
        var itemsConDespacho = new List<OrdenConDespachoItem>();
        if (historial?.Ordenes != null)
        {
            foreach (var ord in historial.Ordenes)
            {
                var desp = await _gateway.ObtenerDespachoOrdenAsync(ord.OrdenId);
                itemsConDespacho.Add(new OrdenConDespachoItem(ord, desp));
            }
        }
        ViewBag.HistorialDespachos = itemsConDespacho;
    }
}
