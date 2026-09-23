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
    string EstadoBadgeClass);

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

        var filtroNormalizado = string.IsNullOrWhiteSpace(filtro) ? "TODOS" : filtro.Trim().ToUpperInvariant();
        ViewBag.Filtro = filtroNormalizado;

        var ubicacion = string.IsNullOrWhiteSpace(ubicacionId) ? "TIENDA-01-CENTRAL" : ubicacionId.Trim();
        ViewBag.UbicacionId = ubicacion;

        // Consultar cola de preparación real del servicio de Despacho (HU-019)
        var colaReal = await _gateway.ObtenerColaPreparacionAsync(ubicacion);

        var todos = (colaReal ?? new List<DespachoPreparacionDto>()).Select(item => new DespachoItemModel(
            IdDespacho: item.DespachoId,
            OrdenId: item.OrdenId,
            Destino: item.MetodoEntrega == "RECOGIDA_TIENDA" ? $"MegaStore {item.UbicacionOrigenId}" : "Envío a Domicilio",
            Metodo: item.MetodoEntrega,
            MetodoLabel: item.MetodoEntrega == "RECOGIDA_TIENDA" ? "Recogida Tienda" : "Domicilio",
            MetodoIcon: item.MetodoEntrega == "RECOGIDA_TIENDA" ? "storefront" : "local_shipping",
            Transportista: item.MetodoEntrega == "RECOGIDA_TIENDA" ? "Flota Interna" : "LogiExpress",
            Guia: item.GuiaId,
            Estado: item.Estado,
            EstadoBadgeClass: MapearBadgeEstado(item.Estado)
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
            EnPreparacion: todos.Count(d => d.Estado == "EN_PREPARACION"),
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

    private static string MapearBadgeEstado(string estado) => estado switch
    {
        "ENTREGADA" or "LISTA_PARA_RECOGER" => "bg-emerald-100 text-emerald-800 border border-emerald-200",
        "EN_TRANSITO" => "bg-purple-100 text-purple-800 border border-purple-200",
        "EN_PREPARACION" => "bg-amber-100 text-amber-800 border border-amber-200",
        _ => "bg-blue-100 text-blue-800 border border-blue-200"
    };
}
