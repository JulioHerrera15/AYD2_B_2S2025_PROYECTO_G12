using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public class CheckoutController : Controller
{
    private readonly GatewayClient _gateway;

    public CheckoutController(GatewayClient gateway)
    {
        _gateway = gateway;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var jwt = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(jwt))
        {
            TempData["Error"] = "Por favor inicia sesión o regístrate para proceder al checkout y completar tu compra de forma segura.";
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Checkout" });
        }

        var userRole = Request.Cookies["sgo_user_role"] ?? "CLIENTE";
        var userEmail = Request.Cookies["sgo_user_email"];
        if (PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt))
        {
            TempData["Info"] = "Las cuentas administrativas gestionan la plataforma y despachos. Para comprar en la tienda, utiliza una cuenta de cliente.";
            return RedirectToAction("Index", "Despacho");
        }

        var carrito = await _gateway.ObtenerCarritoAsync();
        if (carrito is null || carrito.Items is null || carrito.Items.Count == 0)
        {
            TempData["Error"] = "Tu carrito está vacío. Agrega productos antes de proceder al checkout.";
            return RedirectToAction("Index", "Carrito");
        }

        ViewBag.UserEmail = Request.Cookies["sgo_user_email"];
        ViewBag.UserName = Request.Cookies["sgo_user_name"];
        return View(carrito);
    }

    [HttpPost]
    public async Task<IActionResult> Procesar(
        string? clienteEmail,
        string? receptorCui,
        string metodoEntrega,
        string? ubicacionSolicitadaId,
        string? direccion,
        string? departamento,
        string? municipio,
        string? referencia,
        string medioPago,
        string? tokenMedioPago)
    {
        var jwt = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(jwt))
        {
            TempData["Error"] = "Sesión expirada. Inicia sesión nuevamente para procesar tu compra.";
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Checkout" });
        }

        var userRole = Request.Cookies["sgo_user_role"] ?? "CLIENTE";
        var userEmail = Request.Cookies["sgo_user_email"] ?? clienteEmail;
        if (PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt))
        {
            TempData["Info"] = "Las cuentas de administración no realizan compras en línea. Accede al centro de operaciones.";
            return RedirectToAction("Index", "Despacho");
        }

        var carrito = await _gateway.ObtenerCarritoAsync();
        if (carrito is null || carrito.Items is null || carrito.Items.Count == 0)
        {
            TempData["Error"] = "El carrito expiró o está vacío.";
            return RedirectToAction("Index", "Carrito");
        }

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            userEmail = $"invitado-{Guid.NewGuid().ToString("N")[..8]}@megastore.com";
        }

        var cuiFinal = string.IsNullOrWhiteSpace(receptorCui) ? "CF" : receptorCui.Trim();
        var metodoEntregaFinal = string.Equals(metodoEntrega, "RECOGIDA_TIENDA", StringComparison.OrdinalIgnoreCase)
            ? "RECOGIDA_TIENDA"
            : "DOMICILIO";

        var medioPagoFinal = string.IsNullOrWhiteSpace(medioPago)
            ? "TARJETA"
            : (medioPago.Contains("TRANSFERENCIA", StringComparison.OrdinalIgnoreCase) ? "TRANSFERENCIA" : "TARJETA");

        var subtotal = carrito.Items.Sum(i => i.Subtotal);
        var impuestos = Math.Round(subtotal * 0.12m / 1.12m, 2);

        var lineas = carrito.Items.Select(i => new LineaCrearOrdenModel(
            i.Sku,
            i.Descripcion,
            i.Cantidad,
            i.PrecioUnitario
        )).ToList();

        DireccionEntregaModel? direccionModel = null;
        if (metodoEntregaFinal == "DOMICILIO")
        {
            direccionModel = new DireccionEntregaModel(
                Pais: "GT",
                Departamento: string.IsNullOrWhiteSpace(departamento) ? "Guatemala" : departamento.Trim(),
                Municipio: string.IsNullOrWhiteSpace(municipio) ? "Guatemala" : municipio.Trim(),
                Direccion: string.IsNullOrWhiteSpace(direccion) ? "Ciudad de Guatemala" : direccion.Trim(),
                Referencia: string.IsNullOrWhiteSpace(referencia) ? null : referencia.Trim()
            );
        }

        // Regla PCI-DSS (RES-08 / ADR-010): Si es TARJETA, tokenMedioPago es obligatorio
        if (medioPagoFinal == "TARJETA" && string.IsNullOrWhiteSpace(tokenMedioPago))
        {
            ViewBag.Error = "No se recibió el token de la tarjeta. Por favor verifique los datos del medio de pago.";
            ViewBag.UserEmail = userEmail;
            return View("Index", carrito);
        }

        var crearModel = new CrearOrdenModel(
            ClienteId: userEmail,
            Canal: "WEB",
            MetodoEntrega: metodoEntregaFinal,
            Moneda: "GTQ",
            MedioPago: medioPagoFinal,
            Impuestos: impuestos,
            Lineas: lineas,
            UbicacionSolicitadaId: metodoEntregaFinal == "RECOGIDA_TIENDA" ? (ubicacionSolicitadaId ?? "TIENDA-01-CENTRAL") : null,
            ReceptorCui: cuiFinal,
            DireccionEntrega: direccionModel,
            TokenMedioPago: medioPagoFinal == "TARJETA" ? tokenMedioPago : null
        );

        var (exitoCrear, ordenCreada, errorCrear) = await _gateway.CrearOrdenAsync(crearModel);
        if (!exitoCrear || ordenCreada is null)
        {
            ViewBag.Error = $"No se pudo crear la orden: {errorCrear}";
            ViewBag.UserEmail = userEmail;
            return View("Index", carrito);
        }

        // Confirmar la orden para disparar la saga transaccional
        var (exitoConfirmar, ordenConfirmada, errorConfirmar) = await _gateway.ConfirmarOrdenAsync(
            ordenCreada.Id,
            "Checkout completado exitosamente desde la tienda web");

        var ordenFinal = ordenConfirmada ?? ordenCreada;

        return RedirectToAction("Exito", new
        {
            ordenId = ordenFinal.Id,
            numeroOrden = ordenFinal.NumeroOrden,
            total = ordenFinal.Total
        });
    }

    [HttpGet]
    public IActionResult Exito(string ordenId, string? numeroOrden, decimal? total)
    {
        ViewBag.OrdenId = ordenId;
        ViewBag.NumeroOrden = numeroOrden ?? ordenId;
        ViewBag.Total = total ?? 0m;
        return View();
    }
}
