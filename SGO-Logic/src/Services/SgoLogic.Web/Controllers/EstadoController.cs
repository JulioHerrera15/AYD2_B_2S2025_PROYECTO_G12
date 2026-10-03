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
    public async Task<IActionResult> Index(string? ordenId, string? buscar, string? filtro)
    {
        var jwt = Request.Cookies["sgo_jwt"];
        var userEmail = Request.Cookies["sgo_user_email"];
        var userRole = Request.Cookies["sgo_user_role"] ?? "CLIENTE";
        var isAuthenticated = !string.IsNullOrWhiteSpace(jwt) && !string.IsNullOrWhiteSpace(userEmail);
        var isStaff = PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt);

        ViewBag.IsAuthenticated = isAuthenticated;
        ViewBag.UserEmail = userEmail;
        ViewBag.IsStaff = isStaff;
        ViewBag.UserRole = userRole;
        ViewBag.Buscar = buscar;
        ViewBag.Filtro = string.IsNullOrWhiteSpace(filtro) ? "TODOS" : filtro.Trim().ToUpperInvariant();

        // 1. Si no esta autenticado
        if (!isAuthenticated)
        {
            if (!string.IsNullOrWhiteSpace(ordenId))
            {
                return RedirectToAction("Comprobante", new { ordenId = ordenId.Trim() });
            }

            // Muestra pantalla informativa segura sin buscador arbitrario
            return View();
        }

        // 2. Si esta autenticado pero no paso ordenId: listar sus propias guias (o todas si es admin)
        if (string.IsNullOrWhiteSpace(ordenId))
        {
            var identificador = isStaff ? "TODOS" : userEmail!;
            await CargarHistorialUsuarioAsync(identificador, isStaff, buscar, ViewBag.Filtro as string);
            return View();
        }

        // 3. Si paso ordenId: cargar detalle con verificacion estricta de pertenencia
        var idLimpio = ordenId.Trim();
        ViewBag.OrdenId = idLimpio;

        // Tolerancia a consistencia eventual (proyecciones asincronas de CQRS por RabbitMQ):
        // Reintentar brevemente hasta 4 veces si la orden se acaba de crear y la proyeccion esta en proceso
        OrdenEstadoModel? orden = null;
        for (int intento = 0; intento < 4; intento++)
        {
            orden = await _gateway.ObtenerEstadoOrdenAsync(idLimpio);
            if (orden is not null) break;
            await Task.Delay(300);
        }

        if (orden is null)
        {
            ViewBag.Error = $"No se encontró la orden '{idLimpio}' en el sistema.";
            var identificador = isStaff ? "TODOS" : userEmail!;
            await CargarHistorialUsuarioAsync(identificador, isStaff, buscar, ViewBag.Filtro as string);
            return View();
        }

        var esDuenio = string.Equals(orden.ClienteId, userEmail, StringComparison.OrdinalIgnoreCase);

        if (!esDuenio && !isStaff)
        {
            ViewBag.Error = "Acceso denegado: Por razones de privacidad, solo puedes consultar las órdenes asociadas a tu cuenta.";
            await CargarHistorialUsuarioAsync(userEmail!, false, null, null);
            return View();
        }

        var despacho = await _gateway.ObtenerDespachoOrdenAsync(idLimpio);
        ViewBag.Despacho = despacho;
        ViewBag.TiendaOrigen = await ResolverTiendaOrigenAsync(orden, despacho);

        // Reembolso a la tarjeta: se lee siempre de Ordenes (fuente de verdad), asi el
        // banner aparece en cuanto se cancela aunque la vista de estado aun no lo refleje.
        // Vista devuelve null si la orden no tiene reembolso.
        ViewBag.Reembolso = ReembolsoOrden.Vista(await _gateway.ObtenerOrdenAsync(idLimpio));

        if (orden.Estado is "CANCELADA" or "CANCELADA_RESERVA_VENCIDA" or "RECHAZADA" or "ENTREGADA" or "DEVOLUCION_EN_PROCESO" or "DEVUELTA")
        {
            ViewBag.NotasCredito = await _gateway.ObtenerNotasCreditoAsync(idLimpio);
        }

        return View(orden);
    }

    /// <summary>
    /// Nombre de la tienda desde la que sale un envio a domicilio, o null si sale de
    /// un centro de distribucion (lo normal) o aun no se sabe. El origen viene de la
    /// vista de Consulta de Estado y, si todavia no llego, del propio despacho.
    /// </summary>
    private async Task<string?> ResolverTiendaOrigenAsync(OrdenEstadoModel orden, DespachoOrdenModel? despacho)
    {
        if (!string.Equals(orden.MetodoEntrega, "DOMICILIO", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var origenId = orden.UbicacionOrigenId ?? despacho?.UbicacionOrigenId;
        if (string.IsNullOrWhiteSpace(origenId))
        {
            return null;
        }

        var sucursal = (await _gateway.ObtenerSucursalesAsync()).FirstOrDefault(s => s.Id == origenId);
        return sucursal is { EsTienda: true } ? sucursal.Nombre : null;
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
            return Unauthorized(new { Error = "Requiere autenticación para consultar estado" });

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

    private async Task CargarHistorialUsuarioAsync(string identificador, bool esPersonal, string? buscar, string? filtro)
    {
        var tamano = esPersonal ? 100 : 25;
        var historial = await _gateway.ObtenerHistorialClienteAsync(identificador, 1, tamano);
        var itemsConDespacho = new List<OrdenConDespachoItem>();
        if (historial?.Ordenes != null && historial.Ordenes.Count > 0)
        {
            var tareas = historial.Ordenes.Select(async ord =>
            {
                var desp = await _gateway.ObtenerDespachoOrdenAsync(ord.OrdenId);
                return new OrdenConDespachoItem(ord, desp);
            });
            var resultados = await Task.WhenAll(tareas);
            itemsConDespacho.AddRange(resultados);
        }

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var b = buscar.Trim();
            itemsConDespacho = itemsConDespacho.Where(x =>
                (x.Orden.NumeroOrden != null && x.Orden.NumeroOrden.Contains(b, StringComparison.OrdinalIgnoreCase)) ||
                x.Orden.OrdenId.Contains(b, StringComparison.OrdinalIgnoreCase) ||
                (x.Orden.ClienteId != null && x.Orden.ClienteId.Contains(b, StringComparison.OrdinalIgnoreCase)) ||
                (x.Despacho?.GuiaId != null && x.Despacho.GuiaId.Contains(b, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }

        if (!string.IsNullOrWhiteSpace(filtro) && filtro != "TODOS")
        {
            itemsConDespacho = itemsConDespacho.Where(x =>
                string.Equals(x.Orden.Estado, filtro, StringComparison.OrdinalIgnoreCase) ||
                (x.Despacho != null && string.Equals(x.Despacho.Estado, filtro, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }

        ViewBag.HistorialDespachos = itemsConDespacho;
        ViewBag.TotalOrdenes = historial?.TotalRegistros ?? itemsConDespacho.Count;
    }

    /// <summary>
    /// Factura electronica de la orden, legible e imprimible. El dueno de la orden (o
    /// el personal autorizado) la ve completa; un visitante anonimo o un cliente
    /// ajeno solo ve serie, numero y estado, sin CUI ni lineas.
    /// </summary>
    [HttpGet("Estado/Factura")]
    public async Task<IActionResult> Factura(string ordenId)
    {
        if (string.IsNullOrWhiteSpace(ordenId))
        {
            TempData["Error"] = "Identificador de orden requerido.";
            return RedirectToAction("Index");
        }

        var idLimpio = ordenId.Trim();
        var jwt = Request.Cookies["sgo_jwt"];
        var userEmail = Request.Cookies["sgo_user_email"];
        var userRole = Request.Cookies["sgo_user_role"] ?? "CLIENTE";
        var autenticado = !string.IsNullOrWhiteSpace(jwt) && !string.IsNullOrWhiteSpace(userEmail);

        var orden = await _gateway.ObtenerEstadoOrdenAsync(idLimpio);
        var factura = await _gateway.ObtenerFacturaAsync(idLimpio);

        ViewBag.OrdenId = idLimpio;
        ViewBag.NotasCredito = factura is null
            ? new List<FacturaModel>()
            : await _gateway.ObtenerNotasCreditoAsync(idLimpio);
        ViewBag.Orden = orden;
        ViewBag.Autenticado = autenticado;
        ViewBag.VistaCompleta = autenticado && PermisosHelper.PuedeVerOrden(orden, userEmail, userRole, jwt);

        return View(factura);
    }

    [HttpGet("Estado/Comprobante")]
    public async Task<IActionResult> Comprobante(string ordenId)
    {
        if (string.IsNullOrWhiteSpace(ordenId))
        {
            TempData["Error"] = "Identificador de orden requerido.";
            return RedirectToAction("Index");
        }

        var idLimpio = ordenId.Trim();
        ViewBag.OrdenId = idLimpio;

        OrdenEstadoModel? orden = null;
        for (int intento = 0; intento < 5; intento++)
        {
            orden = await _gateway.ObtenerEstadoOrdenAsync(idLimpio);
            if (orden is not null) break;
            await Task.Delay(300);
        }

        if (orden is null)
        {
            ViewBag.Error = $"No se encontró la orden '{idLimpio}' en el sistema.";
            return View();
        }

        var despacho = await _gateway.ObtenerDespachoOrdenAsync(idLimpio);
        ViewBag.Despacho = despacho;

        return View(orden);
    }
}
