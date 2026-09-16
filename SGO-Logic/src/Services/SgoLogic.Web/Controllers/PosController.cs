using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public class PosController : Controller
{
    private readonly GatewayClient _gateway;

    public PosController(GatewayClient gateway)
    {
        _gateway = gateway;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? busqueda, bool demo = false)
    {
        ViewBag.Busqueda = busqueda;
        CarritoModel? carrito = null;

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var term = busqueda.Trim();

            // 1. Si parece un ID de orden (ORD-...) o correlativo numérico
            if (term.StartsWith("ORD-", StringComparison.OrdinalIgnoreCase) || (term.Length == 6 && int.TryParse(term, out _)))
            {
                var ordenId = term.StartsWith("ORD-", StringComparison.OrdinalIgnoreCase) ? term : $"ORD-2026-{term}";
                var orden = await _gateway.ObtenerEstadoOrdenAsync(ordenId);
                if (orden is not null)
                {
                    ViewBag.OrdenEncontrada = orden;
                }
                else
                {
                    ViewBag.Mensaje = $"No se encontró ninguna orden con identificador '{busqueda}'.";
                }
            }
            else
            {
                // 2. Buscar carrito por correo o identificador de cliente / sesión
                carrito = await _gateway.ObtenerCarritoPorIdAsync(term);
                if (carrito is null || carrito.Items is null || carrito.Items.Count == 0)
                {
                    ViewBag.Mensaje = $"No se encontró un carrito activo para '{busqueda}'. Puedes verificar el correo o iniciar una nueva venta.";
                }
                else
                {
                    ViewBag.ExitoBusqueda = $"Carrito recuperado con éxito para '{busqueda}'.";
                }
            }
        }
        else
        {
            // Cargar carrito activo de la sesión si existe para conveniencia de la demo
            carrito = await _gateway.ObtenerCarritoAsync();
        }

        // Solo proveer artículos de demostración si se solicita explícitamente (?demo=true)
        if (demo && (carrito is null || carrito.Items is null || carrito.Items.Count == 0))
        {
            carrito = new CarritoModel(
                ClienteOSesionId: "demo.cliente@megastore.com",
                Items: new List<ItemCarritoModel>
                {
                    new("PROD-001", "Lumina Smart Refrigerator Pro", 1, 14999.00m),
                    new("PROD-002", "Smart Ultra HD LED TV 65\"", 1, 5499.00m)
                },
                Total: 20498.00m
            );
            ViewBag.EsDemo = true;
        }

        return View(carrito);
    }

    [HttpPost]
    public async Task<IActionResult> Cobrar(string clienteId, string medioPago)
    {
        var carrito = await _gateway.ObtenerCarritoPorIdAsync(clienteId) ?? await _gateway.ObtenerCarritoAsync();
        if (carrito is null || carrito.Items is null || carrito.Items.Count == 0)
        {
            TempData["Error"] = "No hay productos para cobrar.";
            return RedirectToAction("Index");
        }

        var total = carrito.Items.Sum(i => i.Subtotal);
        var lineas = carrito.Items.Select(i => new LineaCrearOrdenModel(
            i.Sku,
            i.Descripcion,
            i.Cantidad,
            i.PrecioUnitario
        )).ToList();

        var crearModel = new CrearOrdenModel(
            ClienteId: string.IsNullOrWhiteSpace(clienteId) ? "cliente.pos@megastore.com" : clienteId,
            Canal: "TIENDA",
            MetodoEntrega: "RECOGIDA_TIENDA",
            Moneda: "GTQ",
            MedioPago: string.Equals(medioPago, "TRANSFERENCIA", StringComparison.OrdinalIgnoreCase) ? "TRANSFERENCIA" : "TARJETA",
            Impuestos: Math.Round(total * 0.12m / 1.12m, 2),
            Lineas: lineas,
            UbicacionSolicitadaId: "TIENDA-01-CENTRAL",
            ReceptorCui: "CF"
        );

        var (exitoCrear, ordenCreada, errorCrear) = await _gateway.CrearOrdenAsync(crearModel);
        if (!exitoCrear || ordenCreada is null)
        {
            TempData["Error"] = $"Error al registrar venta POS: {errorCrear}";
            return RedirectToAction("Index", new { busqueda = clienteId });
        }

        await _gateway.ConfirmarOrdenAsync(ordenCreada.Id, "Cobro presencial en terminal POS completado");

        TempData["Exito"] = $"Venta en POS registrada con éxito. Orden: {ordenCreada.NumeroOrden} (Total: Q {ordenCreada.Total:N2})";
        return RedirectToAction("Index", "Estado", new { ordenId = ordenCreada.Id });
    }

    [HttpPost]
    public IActionResult EnviarLinkPago(string clienteId)
    {
        TempData["Exito"] = $"Enlace de pago omnicanal enviado por SMS y WhatsApp al cliente {clienteId}.";
        return RedirectToAction("Index", new { busqueda = clienteId });
    }
}
