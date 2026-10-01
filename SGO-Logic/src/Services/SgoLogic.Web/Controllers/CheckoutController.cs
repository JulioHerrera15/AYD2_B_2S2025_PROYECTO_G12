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
            ViewBag.Error = "Valida tu tarjeta antes de confirmar el pedido.";
            ViewBag.UserEmail = userEmail;
            return View("Index", carrito);
        }

        var ubicacionPreferida = string.IsNullOrWhiteSpace(ubicacionSolicitadaId)
            ? Request.Cookies["sgo_sucursal"]
            : ubicacionSolicitadaId;

        if (string.IsNullOrWhiteSpace(ubicacionPreferida))
        {
            ubicacionPreferida = "TIENDA-01-CENTRAL";
        }

        // Antes de crear la orden se verifica que el stock alcance; si no, la orden
        // ni se crea y el cliente lo sabe aqui en lugar de ver un rechazo despues.
        var sinStock = await BuscarItemSinDisponibleAsync(carrito.Items);
        if (sinStock is not null)
        {
            var nombre = string.IsNullOrWhiteSpace(sinStock.Descripcion) ? sinStock.Sku : sinStock.Descripcion;
            ViewBag.Error = $"El producto {nombre} ya fue reservado previamente y no hay unidades disponibles.";
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
            UbicacionSolicitadaId: ubicacionPreferida,
            ReceptorCui: cuiFinal,
            DireccionEntrega: direccionModel,
            TokenMedioPago: medioPagoFinal == "TARJETA" ? tokenMedioPago : null
        );

        var (exitoCrear, ordenCreada, errorCrear) = await _gateway.CrearOrdenAsync(crearModel);
        if (!exitoCrear || ordenCreada is null)
        {
            ViewBag.Error = $"No pudimos registrar tu pedido: {errorCrear}";
            ViewBag.UserEmail = userEmail;
            return View("Index", carrito);
        }

        // Confirmar la orden dispara la saga. La confirmacion solo deja la orden en
        // RESERVA_SOLICITADA: el resultado del cobro llega despues por eventos, asi
        // que Exito consulta el estado real en lugar de asumir que se cobro.
        // El endpoint es idempotente, por eso se reintenta una vez ante un fallo.
        var (exitoConfirmar, ordenConfirmada, _) = await _gateway.ConfirmarOrdenAsync(
            ordenCreada.Id,
            "Checkout completado desde la tienda web");
        if (!exitoConfirmar)
        {
            (exitoConfirmar, ordenConfirmada, _) = await _gateway.ConfirmarOrdenAsync(
                ordenCreada.Id,
                "Checkout completado desde la tienda web (reintento)");
        }

        var ordenFinal = ordenConfirmada ?? ordenCreada;

        return RedirectToAction("Exito", new
        {
            ordenId = ordenFinal.Id,
            numeroOrden = ordenFinal.NumeroOrden,
            total = ordenFinal.Total,
            confirmada = exitoConfirmar,
            creadaEn = ordenFinal.CreadaEn.ToString("O")
        });
    }

    /// <summary>
    /// Replica el criterio de SelectorUbicacionReserva de Inventario: la reserva sale
    /// de una sola ubicacion, asi que el carrito cabe solo si alguna ubicacion tiene
    /// disponible suficiente para todos sus SKU a la vez. Devuelve el item a
    /// reportar si no cabe (el primero cuyo mejor disponible no alcanza o, si cada
    /// SKU cabe por separado pero en ubicaciones distintas, el primero del carrito),
    /// o null si cabe. Si Inventario no responde no se bloquea la compra: la saga
    /// sigue siendo la que decide y rechaza la reserva si hiciera falta.
    /// </summary>
    private async Task<ItemCarritoModel?> BuscarItemSinDisponibleAsync(IReadOnlyList<ItemCarritoModel> items)
    {
        var pedidoPorSku = items
            .GroupBy(item => item.Sku)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Sum(item => item.Cantidad));

        var disponiblePorUbicacion = new Dictionary<string, Dictionary<string, int>>();

        foreach (var sku in pedidoPorSku.Keys)
        {
            List<ExistenciaSkuDto>? existencias;
            try
            {
                existencias = await _gateway.ObtenerExistenciasSkuAsync(sku);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return null;
            }

            if (existencias is null)
            {
                return null;
            }

            foreach (var existencia in existencias)
            {
                if (!disponiblePorUbicacion.TryGetValue(existencia.UbicacionId, out var porSku))
                {
                    porSku = new Dictionary<string, int>();
                    disponiblePorUbicacion[existencia.UbicacionId] = porSku;
                }

                porSku[sku] = existencia.Disponible;
            }
        }

        var algunaUbicacionCubreTodo = disponiblePorUbicacion.Values.Any(porSku =>
            pedidoPorSku.All(par => porSku.TryGetValue(par.Key, out var disponible) && disponible >= par.Value));

        if (algunaUbicacionCubreTodo)
        {
            return null;
        }

        var skuFaltante = pedidoPorSku
            .Where(par => disponiblePorUbicacion.Values
                .Select(porSku => porSku.TryGetValue(par.Key, out var disponible) ? disponible : 0)
                .DefaultIfEmpty(0)
                .Max() < par.Value)
            .Select(par => par.Key)
            .FirstOrDefault() ?? pedidoPorSku.Keys.First();

        return items.First(item => item.Sku == skuFaltante);
    }

    [HttpGet]
    public IActionResult Exito(string ordenId, string? numeroOrden, decimal? total, bool confirmada = true, string? creadaEn = null)
    {
        ViewBag.OrdenId = ordenId;
        ViewBag.NumeroOrden = numeroOrden ?? ordenId;
        ViewBag.Total = total ?? 0m;
        ViewBag.Confirmada = confirmada;
        if (DateTime.TryParse(creadaEn, out var fecha))
        {
            ViewBag.CreadaEn = fecha;
        }
        else
        {
            ViewBag.CreadaEn = DateTime.UtcNow;
        }
        return View();
    }
}
