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
    List<DespachoPreparacionDto>? ColaPreparacion = null,
    string UbicacionId = PanelDespacho.TodasLasUbicaciones,
    List<string>? Sedes = null,
    string? Error = null);

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

        var ubicacionFiltro = string.IsNullOrWhiteSpace(ubicacionId) ||
                              ubicacionId.Trim().Equals(PanelDespacho.TodasLasUbicaciones, StringComparison.OrdinalIgnoreCase)
            ? PanelDespacho.TodasLasUbicaciones
            : ubicacionId.Trim();
        ViewBag.UbicacionId = ubicacionFiltro;

        // Despachos de todas las ubicaciones de origen (HU-019). El origen lo decide
        // Inventario al reservar, asi que no se puede asumir una lista fija de sedes:
        // se pide la lista completa y la sede elegida se filtra en memoria, lo que
        // tambien permite armar el selector con las sedes reales.
        var despachos = await _gateway.ListarDespachosAsync();
        var model = PanelDespacho.Construir(despachos, filtroNormalizado, ubicacionFiltro);

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
}
