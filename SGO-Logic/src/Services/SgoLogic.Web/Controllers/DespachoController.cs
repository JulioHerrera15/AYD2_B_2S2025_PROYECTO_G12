using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public sealed record DespachoItemModel(
    string IdDespacho,
    string OrdenId,
    string Destino,
    string Metodo, // "DOMICILIO" o "RECOGIDA_TIENDA"
    string MetodoLabel,
    string MetodoIcon,
    string Transportista,
    string? Guia,
    string Estado,
    string EstadoBadgeClass,
    string EstadoLabel,
    bool PuedeEnviarARuta);

public sealed record DespachoViewModel(
    string Filtro,
    int TotalDespachos,
    int EnPreparacion,
    int EnTransito,
    int Entregados,
    int ConteoDomicilio,
    int ConteoTienda,
    List<DespachoItemModel> Items,
    List<DespachoPreparacionDto>? ColaPreparacion = null);

public class DespachoController : Controller
{
    private readonly GatewayClient _gateway;

    public DespachoController(GatewayClient gateway)
    {
        _gateway = gateway;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? filtro, string? ubicacionId)
    {
        var jwt = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(jwt))
        {
            TempData["Error"] = "Acceso restringido. Por favor inicia sesión con credenciales de personal de logística para acceder al panel de despacho.";
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Despacho" });
        }

        var userRole = Request.Cookies["sgo_user_role"];
        var userEmail = Request.Cookies["sgo_user_email"];
        if (!PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt))
        {
            TempData["Error"] = "Acceso denegado. El panel de despacho es exclusivo para personal de bodega y logística de MegaStore.";
            return RedirectToAction("Index", "Home");
        }

        var filtroNormalizado = string.IsNullOrWhiteSpace(filtro) ? "TODOS" : filtro.Trim().ToUpperInvariant();
        ViewBag.Filtro = filtroNormalizado;

        var ubicacionFiltro = string.IsNullOrWhiteSpace(ubicacionId) ? "TODAS" : ubicacionId.Trim();
        ViewBag.UbicacionId = ubicacionFiltro;

        // Consultar cola de preparación real del servicio de Despacho (HU-019)
        var ubicaciones = ubicacionFiltro == "TODAS"
            ? new[] { "CD-01", "TIENDA-01-CENTRAL" }
            : new[] { ubicacionFiltro };

        var itemsAcumulados = new List<DespachoPreparacionDto>();
        foreach (var u in ubicaciones)
        {
            var res = await _gateway.ObtenerColaPreparacionAsync(u);
            if (res != null && res.Count > 0)
            {
                itemsAcumulados.AddRange(res);
            }
        }

        var colaReal = itemsAcumulados
            .GroupBy(x => !string.IsNullOrWhiteSpace(x.DespachoId) ? x.DespachoId : (!string.IsNullOrWhiteSpace(x.Id) ? x.Id : x.OrdenId))
            .Select(g => g.First())
            .OrderByDescending(x => x.CreadoEn)
            .ToList();

        var todos = colaReal.Select(item => new DespachoItemModel(
            IdDespacho: item.DespachoId,
            OrdenId: item.OrdenId,
            Destino: item.MetodoEntrega == "RECOGIDA_TIENDA" 
                ? $"MegaStore Tienda Central" 
                : $"Envío a Domicilio ({item.UbicacionOrigenId})",
            Metodo: item.MetodoEntrega,
            MetodoLabel: item.MetodoEntrega == "RECOGIDA_TIENDA" ? "Recogida Tienda" : "Domicilio",
            MetodoIcon: item.MetodoEntrega == "RECOGIDA_TIENDA" ? "storefront" : "local_shipping",
            Transportista: item.MetodoEntrega == "RECOGIDA_TIENDA" ? "Flota Interna" : "LogiExpress",
            Guia: item.GuiaId,
            Estado: item.Estado,
            EstadoBadgeClass: MapearBadgeEstado(item.Estado),
            EstadoLabel: item.Estado == EstadoAnulado ? "CANCELADO" : item.Estado,
            PuedeEnviarARuta: PuedeEnviarARuta(item.Estado, item.MetodoEntrega)
        )).ToList();

        var filtrados = filtroNormalizado switch
        {
            "DOMICILIO" => todos.Where(d => d.Metodo == "DOMICILIO").ToList(),
            "RECOGIDA_TIENDA" or "TIENDA" => todos.Where(d => d.Metodo == "RECOGIDA_TIENDA").ToList(),
            _ => todos
        };

        var model = new DespachoViewModel(
            Filtro: filtroNormalizado,
            TotalDespachos: todos.Count,
            EnPreparacion: todos.Count(d => d.Estado == "EN_PREPARACION" || d.Estado == "GUIA_ASIGNADA"),
            EnTransito: todos.Count(d => d.Estado == "EN_TRANSITO"),
            Entregados: todos.Count(d => d.Estado == "ENTREGADA" || d.Estado == "LISTA_PARA_RECOGER"),
            ConteoDomicilio: todos.Count(d => d.Metodo == "DOMICILIO"),
            ConteoTienda: todos.Count(d => d.Metodo == "RECOGIDA_TIENDA"),
            Items: filtrados,
            ColaPreparacion: colaReal
        );

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> ActualizarEstado(string ordenId, string nuevoEstado, string? detalle)
    {
        var jwt = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Despacho" });
        }

        if (!PermisosHelper.EsPersonalAutorizado(Request.Cookies["sgo_user_role"], Request.Cookies["sgo_user_email"], jwt))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(ordenId) || string.IsNullOrWhiteSpace(nuevoEstado))
        {
            TempData["Error"] = "OrdenId y Estado son requeridos.";
            return RedirectToAction("Index");
        }

        var (exito, error) = await _gateway.ActualizarEstadoDespachoAsync(
            ordenId.Trim(),
            nuevoEstado.Trim(),
            detalle ?? "Actualización operativa desde panel de control web");

        if (!exito)
        {
            TempData["Error"] = $"No se pudo actualizar el despacho: {error}";
        }
        else
        {
            TempData["Exito"] = $"Despacho de la orden {ordenId} actualizado a {nuevoEstado} exitosamente.";
        }

        return RedirectToAction("Index");
    }

    /// <summary>Estado de Despacho con el que se representa un despacho cancelado.</summary>
    private const string EstadoAnulado = "ANULADA";

    /// <summary>
    /// Estados desde los que el dominio de Despacho (TransicionesEntrega) admite el
    /// paso a ruta: EN_TRANSITO para domicilio y LISTA_PARA_RECOGER para recogida.
    /// Un despacho cancelado nunca muestra el boton.
    /// </summary>
    private static readonly HashSet<string> OrigenesHaciaEnTransito =
        new(StringComparer.OrdinalIgnoreCase) { "EN_PREPARACION", "GUIA_ASIGNADA", "GUIA_PENDIENTE", "INCIDENCIA" };

    private static readonly HashSet<string> OrigenesHaciaListaParaRecoger =
        new(StringComparer.OrdinalIgnoreCase) { "EN_PREPARACION", "RECOGIDA_TIENDA", "INCIDENCIA" };

    private static bool PuedeEnviarARuta(string estado, string metodoEntrega) =>
        metodoEntrega == "RECOGIDA_TIENDA"
            ? OrigenesHaciaListaParaRecoger.Contains(estado)
            : OrigenesHaciaEnTransito.Contains(estado);

    private static string MapearBadgeEstado(string estado) => estado switch
    {
        EstadoAnulado => "bg-gray-100 text-gray-600 border border-gray-300",
        "ENTREGADA" or "LISTA_PARA_RECOGER" => "bg-emerald-100 text-emerald-800 border border-emerald-200",
        "EN_TRANSITO" => "bg-purple-100 text-purple-800 border border-purple-200",
        "EN_PREPARACION" => "bg-amber-100 text-amber-800 border border-amber-200",
        _ => "bg-blue-100 text-blue-800 border border-blue-200"
    };
}
