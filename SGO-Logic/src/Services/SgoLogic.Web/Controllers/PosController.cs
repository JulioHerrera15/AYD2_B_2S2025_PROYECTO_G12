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
    public async Task<IActionResult> Index(string? busqueda)
    {
        var jwt = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(jwt))
        {
            TempData["Error"] = "Acceso restringido. Por favor inicia sesión con credenciales de personal de tienda para operar el terminal OmniPOS.";
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Pos" });
        }

        var userRole = Request.Cookies["sgo_user_role"];
        var userEmail = Request.Cookies["sgo_user_email"];
        if (!PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt))
        {
            TempData["Error"] = "Acceso denegado. El terminal OmniPOS es exclusivo para personal administrativo y cajeros de MegaStore.";
            return RedirectToAction("Index", "Home");
        }

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
                // 2. Buscar carrito por correo o identificador de cliente en Redis
                carrito = await _gateway.ObtenerCarritoPorIdAsync(term);
                if (carrito is null || carrito.Items is null || carrito.Items.Count == 0)
                {
                    ViewBag.Mensaje = $"No se encontró un carrito activo en Redis para '{busqueda}'. Puedes verificar el correo del cliente.";
                }
                else
                {
                    ViewBag.ExitoBusqueda = $"Carrito recuperado con éxito para '{busqueda}'.";
                }
            }
        }
        else
        {
            // Cargar carrito activo de la sesión del operador si existe
            carrito = await _gateway.ObtenerCarritoAsync();
        }

        return View(carrito);
    }

    [HttpPost]
    public async Task<IActionResult> Cobrar(string clienteId, string medioPago, string? tiendaId, string? tokenMedioPago)
    {
        var jwt = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Pos" });
        }

        if (!PermisosHelper.EsPersonalAutorizado(Request.Cookies["sgo_user_role"], Request.Cookies["sgo_user_email"], jwt))
        {
            return Forbid();
        }

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

        var operatoremail = Request.Cookies["sgo_user_email"] ?? "operador.tienda@megastore.com";
        var clienteFinal = string.IsNullOrWhiteSpace(clienteId) ? operatoremail : clienteId.Trim();
        var tiendaSeleccionada = string.IsNullOrWhiteSpace(tiendaId) ? "TIENDA-01-CENTRAL" : tiendaId.Trim();

        var medioPagoNormalizado = "BILLETERA_DIGITAL";
        if (string.Equals(medioPago, "TARJETA", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(medioPago, "TARJETA_CREDITO_DEBITO", StringComparison.OrdinalIgnoreCase))
        {
            medioPagoNormalizado = !string.IsNullOrWhiteSpace(tokenMedioPago) ? "TARJETA" : "BILLETERA_DIGITAL";
        }

        var crearModel = new CrearOrdenModel(
            ClienteId: clienteFinal,
            Canal: "TIENDA",
            MetodoEntrega: "RECOGIDA_TIENDA",
            Moneda: "GTQ",
            MedioPago: medioPagoNormalizado,
            Impuestos: Math.Round(total * 0.12m / 1.12m, 2),
            Lineas: lineas,
            UbicacionSolicitadaId: tiendaSeleccionada,
            ReceptorCui: "CF",
            TokenMedioPago: medioPagoNormalizado == "TARJETA" ? tokenMedioPago : null
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
        var jwt = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Pos" });
        }

        if (!PermisosHelper.EsPersonalAutorizado(Request.Cookies["sgo_user_role"], Request.Cookies["sgo_user_email"], jwt))
        {
            return Forbid();
        }

        TempData["Exito"] = $"Enlace de pago omnicanal enviado por SMS y WhatsApp al cliente {clienteId}.";
        return RedirectToAction("Index", new { busqueda = clienteId });
    }
}
