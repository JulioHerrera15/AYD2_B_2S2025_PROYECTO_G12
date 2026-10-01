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

public class CatalogoController : Controller
{
    private readonly GatewayClient _gateway;

    public static readonly List<ProductoCatalogo> Productos =
    [
        new("PROD-001", "Lumina Smart Refrigerator Pro", "Línea Blanca", 14999.00m,
            "Refrigeradora inteligente de 28 pies cúbicos, compuerta French Door, panel táctil Wi-Fi y enfriamiento Twin Cooling Plus.",
            "kitchen", "Más Vendido"),

        new("PROD-002", "Smart Ultra HD LED TV 65\"", "Televisores", 5499.00m,
            "Pantalla 4K UHD con HDR10+, tasa de refresco de 120Hz nativos, Dolby Atmos y sistema operativo Google TV integrado.",
            "tv", "Oferta"),

        new("PROD-003", "Lavadora de Carga Frontal EcoBubble 22kg", "Línea Blanca", 7899.00m,
            "Motor Digital Inverter con 10 años de garantía, lavado con vapor higiénico y dispensador automático de detergente.",
            "local_laundry_service", "Nuevo"),

        new("PROD-004", "Barra de Sonido Dolby Atmos 5.1.2 550W", "Audio", 3299.00m,
            "Subwoofer inalámbrico potente, altavoces traseros satelitales, HDMI eARC y compatibilidad con AirPlay 2 y Spotify Connect.",
            "speaker", "Popular"),

        new("PROD-005", "Laptop Gamer Ultra 16\" Ryzen 9", "Informática", 16499.00m,
            "Procesador AMD Ryzen 9 7945HX, GPU NVIDIA RTX 4070 8GB, 32GB RAM DDR5, 1TB NVMe Gen4 y pantalla 240Hz QHD.",
            "laptop_chromebook", "Gamer"),

        new("PROD-006", "Aire Acondicionado Inverter 18000 BTU", "Climatización", 4599.00m,
            "Tecnología WindFree silenciosa, filtro antibacteriano de alta densidad, modo frío/calor y control remoto por aplicación móvil.",
            "ac_unit", "Ahorro Energético"),

        new("PROD-007", "Microondas Grill Smart Inverter 42L", "Línea Blanca", 1899.00m,
            "Capacidad de 1.5 pies cúbicos, recubrimiento EasyClean antibacteriano, función de dorador al carbón y descongelado uniforme.",
            "microwave", "Básico"),

        new("PROD-008", "Aspiradora Robot Láser con Fregado", "Hogar Inteligente", 3899.00m,
            "Navegación LiDAR de alta precisión, succión de 5000Pa, mapeo multinivel en 3D y estación de autovaciado de polvo.",
            "cleaning_services", "Smart")
    ];

    public CatalogoController(GatewayClient gateway)
    {
        _gateway = gateway;
    }

    [HttpGet]
    public IActionResult Index(string? categoria, string? busqueda)
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

        var filtrados = Productos.AsEnumerable();

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

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Stock(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return BadRequest(new { Error = "SKU requerido" });

        var existencias = await _gateway.ObtenerExistenciasSkuAsync(sku.Trim());
        return Json(existencias ?? new List<ExistenciaSkuDto>());
    }
}
