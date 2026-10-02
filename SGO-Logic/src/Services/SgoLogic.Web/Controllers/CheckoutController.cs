using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public class CheckoutController : Controller
{
    private const string MensajeReservaNoRenovable =
        "Tu reserva venció y algunos productos ya no tienen existencias para apartarse de nuevo. Revisa tu carrito antes de pagar.";

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

        carrito = await AsegurarReservaVigenteAsync(carrito);
        if (carrito is null)
        {
            TempData["Error"] = MensajeReservaNoRenovable;
            return RedirectToAction("Index", "Carrito");
        }

        ViewBag.UserEmail = Request.Cookies["sgo_user_email"];
        ViewBag.UserName = Request.Cookies["sgo_user_name"];
        await CargarSucursalesAsync(carrito);
        return View(carrito);
    }

    /// <summary>
    /// Datos de la vista de checkout: catalogo de tiendas para el selector de recogida
    /// (cacheado 5 minutos por GatewayClient) y, con sesion, las tarjetas guardadas
    /// del cliente. Un invitado no ve tarjetas ni la opcion de guardarlas.
    /// </summary>
    private async Task CargarSucursalesAsync(CarritoModel carrito)
    {
        ViewBag.OpcionCredito = await CargarOpcionCreditoAsync(carrito);

        ViewBag.Sucursales = await _gateway.ObtenerSucursalesAsync();
        ViewBag.SucursalSeleccionada = Request.Cookies["sgo_sucursal"];

        var autenticado = !string.IsNullOrWhiteSpace(Request.Cookies["sgo_jwt"]);
        ViewBag.Autenticado = autenticado;
        ViewBag.Tarjetas = autenticado
            ? await _gateway.ListarTarjetasAsync()
            : Array.Empty<TarjetaGuardadaModel>();
    }

    /// <summary>
    /// Opcion "Credito MegaStore" del checkout. Se consulta con el mismo total que
    /// Ordenes le pedira evaluar a Credito (ver <see cref="TotalOrden" />), con limite
    /// de 1 segundo: si Credito no responde, la opcion queda deshabilitada y el resto
    /// del checkout sigue igual.
    /// </summary>
    private async Task<OpcionCreditoCheckout> CargarOpcionCreditoAsync(CarritoModel carrito)
    {
        if (string.IsNullOrWhiteSpace(Request.Cookies["sgo_jwt"]))
        {
            return new OpcionCreditoCheckout(EstadoOpcionCredito.SinSesion, null, OpcionCreditoCheckout.MensajeSinSesion);
        }

        var total = TotalOrden(carrito);
        if (total <= 0m)
        {
            return new OpcionCreditoCheckout(
                EstadoOpcionCredito.ServicioNoDisponible, null, OpcionCreditoCheckout.MensajeServicioNoDisponible);
        }

        return OpcionCreditoCheckout.Desde(await _gateway.ConsultarOfertaCreditoAsync(total, HttpContext.RequestAborted));
    }

    private static decimal ImpuestosDe(decimal subtotal) => Math.Round(subtotal * 0.12m / 1.12m, 2);

    /// <summary>
    /// Total con el que Ordenes crea la orden: suma de lineas mas los impuestos que
    /// manda Procesar. Es el monto que Credito evalua y el cargo que queda en la cuenta.
    /// </summary>
    private static decimal TotalOrden(CarritoModel carrito)
    {
        var subtotal = carrito.Items?.Sum(i => i.Subtotal) ?? 0m;
        return subtotal + ImpuestosDe(subtotal);
    }

    private const string MensajeSesionCredito = "Tu sesión expiró. Inicia sesión de nuevo para pagar con crédito.";

    /// <summary>Solo estos medios se aceptan desde la web; vacio es TARJETA, como antes.</summary>
    private static string? NormalizarMedioPago(string? medioPago)
    {
        if (string.IsNullOrWhiteSpace(medioPago))
        {
            return "TARJETA";
        }

        var valor = medioPago.Trim().ToUpperInvariant();
        return valor is "TARJETA" or "TRANSFERENCIA" or "CREDITO" ? valor : null;
    }

    // ── Tokenizacion (AJAX desde el checkout) ─────────────────────────────────
    // El navegador ya no habla con PayKlik: manda la tarjeta a la Web, la Web la pasa
    // a Pagos (POST /api/pagos/tokens) y Pagos la valida, la cifra y devuelve un token.
    // El PAN y el CVV no se guardan, no se registran y no vuelven en la respuesta.

    /// <summary>
    /// Tokeniza una tarjeta nueva en Pagos. 200 con {token, marca, ultimos4, expiraEn};
    /// 422 con {errores:{campo:mensaje}} para mostrarlos debajo de cada campo. Con
    /// sesion, guardar=true pide guardar la tarjeta cuando el cobro se apruebe.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Tokenizar(
        string? numero,
        int? mesExp,
        int? anioExp,
        string? cvv,
        string? titular,
        bool guardar = false)
    {
        var autenticado = !string.IsNullOrWhiteSpace(Request.Cookies["sgo_jwt"]);
        var resultado = await _gateway.TokenizarTarjetaAsync(
            new SolicitudTokenPagoModel(numero, mesExp, anioExp, cvv, titular, guardar && autenticado));

        return RespuestaTokenizacion(resultado, "No pudimos validar tu tarjeta en este momento. Intenta de nuevo en unos momentos.");
    }

    /// <summary>Token de la boveda de Pagos para pagar con una tarjeta guardada. Misma forma que Tokenizar.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TokenTarjetaGuardada(Guid id, string? cvv)
    {
        if (string.IsNullOrWhiteSpace(Request.Cookies["sgo_jwt"]))
        {
            return StatusCode(StatusCodes.Status401Unauthorized, new { mensaje = "Inicia sesión para usar tus tarjetas guardadas." });
        }

        var resultado = await _gateway.ObtenerTokenTarjetaAsync(id, cvv ?? string.Empty);
        return RespuestaTokenizacion(resultado, "No se pudo usar la tarjeta guardada.");
    }

    private IActionResult RespuestaTokenizacion(ResultadoTokenPagoModel resultado, string mensajePorDefecto)
    {
        if (resultado.Token is not null)
        {
            return Json(resultado.Token);
        }

        if (resultado.Status == StatusCodes.Status422UnprocessableEntity)
        {
            return UnprocessableEntity(new
            {
                mensaje = resultado.Mensaje ?? "Revisa los datos de la tarjeta.",
                errores = resultado.Errores ?? new Dictionary<string, string>()
            });
        }

        return StatusCode(
            resultado.Status is >= 400 and < 600 ? resultado.Status : StatusCodes.Status502BadGateway,
            new { mensaje = resultado.Mensaje ?? mensajePorDefecto });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarTarjeta(Guid id)
    {
        var (exito, error) = await _gateway.EliminarTarjetaAsync(id);
        return exito
            ? Json(new { exito = true })
            : StatusCode(StatusCodes.Status502BadGateway, new { exito = false, mensaje = error });
    }

    /// <summary>
    /// Si algun item del carrito tiene la reserva vencida, intenta renovarla antes de
    /// seguir. Devuelve el carrito actualizado, o null si no se pudo renovar todo.
    /// </summary>
    private async Task<CarritoModel?> AsegurarReservaVigenteAsync(CarritoModel carrito)
    {
        if (!carrito.TieneReservasVencidas)
        {
            return carrito;
        }

        var (resultado, renovado) = await _gateway.RenovarReservaCarritoAsync();
        if (!resultado.Exito || renovado is null || renovado.TieneReservasVencidas)
        {
            return null;
        }

        return renovado;
    }

    /// <summary>
    /// Simula (sin reservar) desde donde saldria el pedido con el metodo de entrega
    /// elegido. La consulta el checkout por AJAX al cargar y al cambiar el metodo o la
    /// tienda, para avisar si el envio sale de otra tienda o si la tienda de recogida
    /// no tiene todo.
    /// </summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Simular(string? metodoEntrega, string? ubicacionSolicitadaId)
    {
        var carrito = await _gateway.ObtenerCarritoAsync();
        if (carrito?.Items is null || carrito.Items.Count == 0)
        {
            return NoContent();
        }

        var metodo = NormalizarMetodoEntrega(metodoEntrega);
        var simulacion = await SimularAsync(carrito, metodo, ubicacionSolicitadaId);

        if (simulacion is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { mensaje = "No pudimos consultar la disponibilidad en este momento." });
        }

        return Json(new
        {
            metodoEntrega = metodo,
            simulacion.Factible,
            simulacion.UbicacionId,
            simulacion.NombreUbicacion,
            simulacion.TipoUbicacion,
            simulacion.EsOtraSucursal,
            simulacion.Motivo
        });
    }

    private Task<SimulacionReservaModel?> SimularAsync(CarritoModel carrito, string metodoEntrega, string? ubicacionSolicitadaId)
    {
        var referencia = Request.Cookies["sgo_sucursal"];
        var preferida = UbicacionPreferida(metodoEntrega, ubicacionSolicitadaId);

        return _gateway.SimularReservaAsync(new SimularReservaModel(
            carrito.CarritoIdReserva,
            metodoEntrega,
            preferida,
            string.IsNullOrWhiteSpace(referencia) ? preferida : referencia,
            carrito.Items!
                .GroupBy(item => item.Sku)
                .Select(grupo => new LineaSimulacionModel(grupo.Key, grupo.Sum(item => item.Cantidad)))
                .ToList()));
    }

    private static string NormalizarMetodoEntrega(string? metodoEntrega) =>
        string.Equals(metodoEntrega, "RECOGIDA_TIENDA", StringComparison.OrdinalIgnoreCase)
            ? "RECOGIDA_TIENDA"
            : "DOMICILIO";

    /// <summary>
    /// Solo la recogida en tienda lleva ubicacion preferida. A domicilio no se manda
    /// ninguna, para que Inventario priorice el centro de distribucion.
    /// </summary>
    private string? UbicacionPreferida(string metodoEntrega, string? ubicacionSolicitadaId)
    {
        if (metodoEntrega != "RECOGIDA_TIENDA")
        {
            return null;
        }

        var preferida = string.IsNullOrWhiteSpace(ubicacionSolicitadaId)
            ? Request.Cookies["sgo_sucursal"]
            : ubicacionSolicitadaId.Trim();

        return string.IsNullOrWhiteSpace(preferida) ? CatalogoController.SucursalPorDefecto : preferida;
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

        // Si la reserva del carrito vencio mientras el cliente llenaba el formulario,
        // se intenta renovar antes de crear la orden.
        carrito = await AsegurarReservaVigenteAsync(carrito);
        if (carrito is null)
        {
            TempData["Error"] = MensajeReservaNoRenovable;
            return RedirectToAction("Index", "Carrito");
        }

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            userEmail = $"invitado-{Guid.NewGuid().ToString("N")[..8]}@megastore.com";
        }

        var cuiFinal = string.IsNullOrWhiteSpace(receptorCui) ? "CF" : receptorCui.Trim();
        var metodoEntregaFinal = NormalizarMetodoEntrega(metodoEntrega);

        var medioPagoFinal = NormalizarMedioPago(medioPago);
        if (medioPagoFinal is null)
        {
            ViewBag.Error = "Elige un medio de pago válido.";
            ViewBag.UserEmail = userEmail;
            await CargarSucursalesAsync(carrito);
            return View("Index", carrito);
        }

        var subtotal = carrito.Items!.Sum(i => i.Subtotal);
        var impuestos = ImpuestosDe(subtotal);

        var lineas = carrito.Items!.Select(i => new LineaCrearOrdenModel(
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

        // Regla PCI-DSS (RES-08 / ADR-010): con TARJETA el token de la boveda de Pagos es
        // obligatorio; la orden nunca lleva el numero ni el CVV.
        if (medioPagoFinal == "TARJETA" && string.IsNullOrWhiteSpace(tokenMedioPago))
        {
            ViewBag.Error = "Valida tu tarjeta antes de confirmar el pedido.";
            ViewBag.UserEmail = userEmail;
            await CargarSucursalesAsync(carrito);
            return View("Index", carrito);
        }

        // A domicilio no se envia ubicacion: Inventario prioriza el centro de
        // distribucion. Solo la recogida en tienda fija la sucursal.
        var ubicacionPreferida = UbicacionPreferida(metodoEntregaFinal, ubicacionSolicitadaId);

        // Antes de crear la orden se simula la reserva (contando lo que este carrito ya
        // tiene apartado): si ninguna ubicacion puede cubrir el pedido, la orden ni se
        // crea y el cliente lo sabe aqui en lugar de ver un rechazo despues. Si
        // Inventario no responde no se bloquea la compra: la saga decide.
        var simulacion = await SimularAsync(carrito, metodoEntregaFinal, ubicacionSolicitadaId);
        if (simulacion is not null && string.IsNullOrWhiteSpace(simulacion.UbicacionId))
        {
            ViewBag.Error = "No hay unidades suficientes para todo tu pedido en una misma ubicación. Revisa las cantidades de tu carrito.";
            ViewBag.UserEmail = userEmail;
            await CargarSucursalesAsync(carrito);
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
            TokenMedioPago: medioPagoFinal == "TARJETA" ? tokenMedioPago : null,
            CarritoId: carrito.CarritoIdReserva
        );

        var creacion = await _gateway.CrearOrdenConEstadoAsync(crearModel);
        var ordenCreada = creacion.Orden;
        if (!creacion.Exito || ordenCreada is null)
        {
            // Ordenes exige sesion y cliente coincidente solo para credito.
            ViewBag.Error = medioPagoFinal == "CREDITO" && creacion.Status is 401 or 403
                ? MensajeSesionCredito
                : $"No pudimos registrar tu pedido: {creacion.Error}";
            ViewBag.UserEmail = userEmail;
            await CargarSucursalesAsync(carrito);
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
            creadaEn = ordenFinal.CreadaEn.ToString("O"),
            credito = medioPagoFinal == "CREDITO"
        });
    }

    /// <summary>
    /// "Pagar con tarjeta" despues de un rechazo de credito. Confirmar la orden vacio
    /// el carrito, asi que se rearma desde las lineas de la orden (solo lo que falte)
    /// y se vuelve al checkout. La orden debe ser del cliente de la sesion.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PagarConTarjeta(string ordenId)
    {
        var userEmail = Request.Cookies["sgo_user_email"];
        if (string.IsNullOrWhiteSpace(Request.Cookies["sgo_jwt"]) || string.IsNullOrWhiteSpace(userEmail))
        {
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Checkout" });
        }

        var orden = string.IsNullOrWhiteSpace(ordenId) ? null : await _gateway.ObtenerOrdenAsync(ordenId.Trim());
        if (orden is null || !string.Equals(orden.ClienteId, userEmail, StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "No encontramos ese pedido. Revisa tu carrito para continuar.";
            return RedirectToAction("Index", "Carrito");
        }

        var carrito = await _gateway.ObtenerCarritoAsync();
        var enCarrito = (carrito?.Items ?? new List<ItemCarritoModel>())
            .GroupBy(item => item.Sku, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Sum(item => item.Cantidad), StringComparer.OrdinalIgnoreCase);

        var sinStock = new List<string>();
        foreach (var linea in orden.Lineas ?? new List<LineaOrdenModel>())
        {
            var faltan = linea.Cantidad - enCarrito.GetValueOrDefault(linea.Sku);
            if (faltan <= 0)
            {
                continue;
            }

            var agregado = await _gateway.AgregarItemCarritoAsync(
                new AgregarItemModel(linea.Sku, linea.Descripcion, faltan, linea.PrecioUnitario));
            if (!agregado.Exito)
            {
                sinStock.Add(linea.Descripcion);
            }
        }

        if (sinStock.Count > 0)
        {
            TempData["Error"] = $"Algunos productos ya no tienen existencias: {string.Join(", ", sinStock)}. Revisa tu carrito.";
            return RedirectToAction("Index", "Carrito");
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Exito(string ordenId, string? numeroOrden, decimal? total, bool confirmada = true, string? creadaEn = null, bool credito = false)
    {
        ViewBag.Credito = credito;
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
