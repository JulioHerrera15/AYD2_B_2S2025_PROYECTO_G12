using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public sealed record ProductoCatalogo(
    string Sku,
    string Nombre,
    string Categoria,
    decimal PrecioUnitario,
    string DescripcionCorta,
    string Icono,
    string Badge);

/// <summary>
/// Catalogo de la tienda. Los productos y sus precios salen de Inventario
/// (GET /api/inventario/productos, cacheado 5 minutos por GatewayClient): la Web ya
/// no tiene una lista fija ni decide precios.
/// </summary>
public class CatalogoController : Controller
{
    private readonly GatewayClient _gateway;

    /// <summary>Sucursal con la que arranca el catalogo si el cliente aun no eligio una.</summary>
    public const string SucursalPorDefecto = "TIENDA-01-CENTRAL";

    public CatalogoController(GatewayClient gateway)
    {
        _gateway = gateway;
    }

    /// <summary>Productos activos de Inventario como los muestran el catalogo y el POS.</summary>
    public static async Task<List<ProductoCatalogo>> CargarProductosAsync(GatewayClient gateway, CancellationToken ct = default)
    {
        var productos = await gateway.ObtenerProductosAsync(ct);
        return productos
            .Where(producto => producto.Activo)
            .Select(producto => new ProductoCatalogo(
                producto.Sku,
                producto.Nombre,
                producto.Categoria,
                producto.Precio,
                producto.Descripcion ?? string.Empty,
                producto.Icono,
                producto.Etiqueta ?? string.Empty))
            .ToList();
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? categoria, string? busqueda)
    {
        var jwt = Request.Cookies["sgo_jwt"];
        var userRole = Request.Cookies["sgo_user_role"] ?? "CLIENTE";
        var userEmail = Request.Cookies["sgo_user_email"];
        if (PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt))
        {
            TempData["Info"] = "Las cuentas de administración operan en la consola de despacho y monitoreo. El catálogo de compras es para clientes.";
            return RedirectToAction("Index", "Despacho");
        }

        ViewBag.Categoria = categoria;
        ViewBag.Busqueda = busqueda;
        ViewBag.Sucursales = await _gateway.ObtenerSucursalesAsync();
        ViewBag.SucursalSeleccionada = string.IsNullOrWhiteSpace(Request.Cookies["sgo_sucursal"])
            ? SucursalPorDefecto
            : Request.Cookies["sgo_sucursal"];

        var productos = await CargarProductosAsync(_gateway);
        if (productos.Count == 0)
        {
            // Lo muestra el layout en esta misma respuesta.
            TempData["Error"] = "No pudimos cargar el catálogo en este momento. Intenta de nuevo en unos minutos.";
        }

        var filtrados = productos.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(categoria) && !string.Equals(categoria, "TODOS", StringComparison.OrdinalIgnoreCase))
        {
            filtrados = filtrados.Where(p => string.Equals(p.Categoria, categoria, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var term = busqueda.Trim();
            filtrados = filtrados.Where(p =>
                p.Nombre.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Sku.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.DescripcionCorta.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return View(filtrados.ToList());
    }

    /// <summary>
    /// Existencias del SKU en todas las ubicaciones. Si se indica la sucursal
    /// seleccionada y ahi esta agotado, incluye la tienda mas cercana que si tiene
    /// (o null si ninguna tienda tiene).
    /// </summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Stock(string sku, string? ubicacionId)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return BadRequest(new { Error = "SKU requerido" });

        var skuLimpio = sku.Trim();
        var existencias = await _gateway.ObtenerExistenciasSkuAsync(skuLimpio) ?? new List<ExistenciaSkuDto>();

        SucursalCercanaModel? cercana = null;
        if (!string.IsNullOrWhiteSpace(ubicacionId))
        {
            var aqui = existencias.FirstOrDefault(e => e.UbicacionId == ubicacionId);
            if (aqui is null || aqui.Disponible <= 0)
            {
                cercana = await _gateway.ObtenerSucursalCercanaAsync(skuLimpio, ubicacionId.Trim());
            }
        }

        return Json(new { existencias, cercana });
    }
}
