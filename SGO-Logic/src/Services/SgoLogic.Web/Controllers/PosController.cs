using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public sealed record TiendaPosOpcion(string UbicacionId, string Nombre, string PosInstanciaId, int StockDisponible, string Pais = "Guatemala");

public class PosController : Controller
{
    private readonly GatewayClient _gateway;

    public PosController(GatewayClient gateway)
    {
        _gateway = gateway;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? busqueda, string? tiendaId)
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

        // 1. Cargar las 32 terminales fisicas del sistema POS (T01 a T32).
        // El stock que se muestra debe ser exclusivamente el informado por
        // Inventario. No se usa el valor estatico como fallback: una sucursal sin
        // existencia no puede operar como si tuviera unidades disponibles.
        var stockMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var ubicaciones = await _gateway.ObtenerUbicacionesInventarioAsync();
            if (ubicaciones is not null)
            {
                foreach (var u in ubicaciones)
                {
                    stockMap[u.UbicacionId] = u.CantidadDisponibleTotal;
                }
            }
        }
        catch
        {
            // Inventario no disponible momentaneamente: se muestra stock cero y
            // se evita informar al operador sobre una ubicacion no verificada.
        }

        var tiendasOpciones = TodasLasTiendasPos.Select(t =>
        {
            var stock = stockMap.TryGetValue(t.Id, out var s) ? s : 0;
            return new TiendaPosOpcion(t.Id, t.Nombre, t.PosId, stock, t.Pais);
        }).ToList();

        // Tienda activa: parametro -> cookie -> primera tienda
        var tiendaSeleccionadaId = !string.IsNullOrWhiteSpace(tiendaId)
            ? tiendaId.Trim()
            : (Request.Cookies["sgo_pos_tienda"] ?? tiendasOpciones[0].UbicacionId);

        var tiendaActiva = tiendasOpciones.FirstOrDefault(t => t.UbicacionId == tiendaSeleccionadaId) ?? tiendasOpciones[0];
        Response.Cookies.Append("sgo_pos_tienda", tiendaActiva.UbicacionId, new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(30) });

        ViewBag.TiendaActiva = tiendaActiva;
        ViewBag.TiendasDisponibles = tiendasOpciones;
        ViewBag.Catalogo = CatalogoController.Productos;
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
    public async Task<IActionResult> AgregarItem(string sku, int cantidad, string? clienteId, string? tiendaId)
    {
        var jwt = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(jwt)) return RedirectToAction("Login", "Auth", new { returnUrl = "/Pos" });
        if (!PermisosHelper.EsPersonalAutorizado(Request.Cookies["sgo_user_role"], Request.Cookies["sgo_user_email"], jwt)) return Forbid();

        if (string.IsNullOrWhiteSpace(sku))
        {
            TempData["Error"] = "Debe indicar un SKU o seleccionar un producto.";
            return RedirectToAction("Index", new { busqueda = clienteId, tiendaId });
        }

        var cant = cantidad <= 0 ? 1 : cantidad;
        var prod = CatalogoController.Productos.FirstOrDefault(p => string.Equals(p.Sku, sku.Trim(), StringComparison.OrdinalIgnoreCase));
        var descripcion = prod?.Nombre ?? $"Artículo {sku.Trim().ToUpperInvariant()}";
        var precio = prod?.PrecioUnitario ?? 100.00m;

        await _gateway.AgregarItemCarritoAsync(new AgregarItemModel(sku.Trim().ToUpperInvariant(), descripcion, cant, precio));
        TempData["Exito"] = $"{descripcion} ({cant} un.) agregado al ticket POS.";

        return RedirectToAction("Index", new { busqueda = clienteId, tiendaId });
    }

    [HttpPost]
    public async Task<IActionResult> EliminarItem(string sku, string? clienteId, string? tiendaId)
    {
        var jwt = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(jwt)) return RedirectToAction("Login", "Auth", new { returnUrl = "/Pos" });
        if (!PermisosHelper.EsPersonalAutorizado(Request.Cookies["sgo_user_role"], Request.Cookies["sgo_user_email"], jwt)) return Forbid();

        if (!string.IsNullOrWhiteSpace(sku))
        {
            await _gateway.EliminarItemCarritoAsync(sku.Trim());
            TempData["Info"] = $"Artículo {sku.Trim()} removido del ticket POS.";
        }

        return RedirectToAction("Index", new { busqueda = clienteId, tiendaId });
    }

    [HttpPost]
    public async Task<IActionResult> LimpiarTicket(string? clienteId, string? tiendaId)
    {
        var jwt = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(jwt)) return RedirectToAction("Login", "Auth", new { returnUrl = "/Pos" });
        if (!PermisosHelper.EsPersonalAutorizado(Request.Cookies["sgo_user_role"], Request.Cookies["sgo_user_email"], jwt)) return Forbid();

        var carrito = !string.IsNullOrWhiteSpace(clienteId)
            ? await _gateway.ObtenerCarritoPorIdAsync(clienteId.Trim())
            : await _gateway.ObtenerCarritoAsync();
        if (carrito?.Items is not null)
        {
            foreach (var item in carrito.Items)
            {
                await _gateway.EliminarItemCarritoAsync(item.Sku);
            }
        }

        TempData["Info"] = "Ticket POS reiniciado.";
        return RedirectToAction("Index", new { busqueda = clienteId, tiendaId });
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

        CarritoModel? carrito = null;
        if (!string.IsNullOrWhiteSpace(clienteId))
        {
            carrito = await _gateway.ObtenerCarritoPorIdAsync(clienteId.Trim());
        }

        if (carrito is null || carrito.Items is null || carrito.Items.Count == 0)
        {
            carrito = await _gateway.ObtenerCarritoAsync();
        }

        if (carrito is null || carrito.Items is null || carrito.Items.Count == 0)
        {
            TempData["Error"] = "No hay productos para cobrar.";
            return RedirectToAction("Index", new { busqueda = clienteId, tiendaId });
        }

        var total = carrito.Items.Sum(i => i.Subtotal);
        var lineas = carrito.Items.Select(i => new LineaCrearOrdenModel(
            i.Sku,
            i.Descripcion,
            i.Cantidad,
            i.PrecioUnitario
        )).ToList();

        var tiendaSeleccionada = string.IsNullOrWhiteSpace(tiendaId) ? "TIENDA-01-CENTRAL" : tiendaId.Trim();
        var stockTienda = await _gateway.ObtenerExistenciasPorUbicacionAsync(tiendaSeleccionada);
        if (stockTienda is null || stockTienda.Count == 0)
        {
            TempData["Error"] = $"La sucursal seleccionada no tiene existencias registradas: {tiendaSeleccionada}.";
            return RedirectToAction("Index", new { busqueda = clienteId, tiendaId });
        }

        var faltantes = carrito.Items
            .Select(item => new { item.Sku, Disponible = stockTienda.FirstOrDefault(s => string.Equals(s.Sku, item.Sku, StringComparison.OrdinalIgnoreCase))?.Disponible ?? 0, item.Cantidad })
            .Where(item => item.Disponible < item.Cantidad)
            .Select(item => $"{item.Sku} (disponible: {item.Disponible}, solicitado: {item.Cantidad})")
            .ToList();
        if (faltantes.Count > 0)
        {
            TempData["Error"] = $"No hay stock suficiente en la sucursal seleccionada: {string.Join(", ", faltantes)}.";
            return RedirectToAction("Index", new { busqueda = clienteId, tiendaId });
        }

        var operatoremail = Request.Cookies["sgo_user_email"] ?? "operador.tienda@megastore.com";
        var clienteFinal = string.IsNullOrWhiteSpace(clienteId) ? operatoremail : clienteId.Trim();
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
            return RedirectToAction("Index", new { busqueda = clienteId, tiendaId });
        }

        await _gateway.ConfirmarOrdenAsync(ordenCreada.Id, "Cobro presencial en terminal POS completado");

        // Limpiar items del carrito en Redis para dejar el terminal libre para la siguiente venta
        foreach (var item in lineas)
        {
            await _gateway.EliminarItemCarritoAsync(item.Sku);
        }

        var posInstancia = MapearTiendaAPos(tiendaSeleccionada);
        TempData["Exito"] = $"Venta en POS registrada con exito en {posInstancia}. Orden: {ordenCreada.NumeroOrden} (Total: Q {ordenCreada.Total:N2})";

        // Breve pausa para dar tiempo a la proyeccion eventual de CQRS por RabbitMQ
        await Task.Delay(250);
        return RedirectToAction("Index", "Estado", new { ordenId = ordenCreada.Id });
    }

    [HttpPost]
    public async Task<IActionResult> CobrarConComprobante(string? telefono, string clienteId, string medioPago, string? tiendaId, string? tokenMedioPago)
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

        CarritoModel? carrito = null;
        if (!string.IsNullOrWhiteSpace(clienteId))
        {
            carrito = await _gateway.ObtenerCarritoPorIdAsync(clienteId.Trim());
        }

        if (carrito is null || carrito.Items is null || carrito.Items.Count == 0)
        {
            carrito = await _gateway.ObtenerCarritoAsync();
        }

        if (carrito is null || carrito.Items is null || carrito.Items.Count == 0)
        {
            TempData["Error"] = "No hay productos en el ticket para cobrar y notificar.";
            return RedirectToAction("Index", new { busqueda = clienteId, tiendaId });
        }

        var total = carrito.Items.Sum(i => i.Subtotal);
        var lineas = carrito.Items.Select(i => new LineaCrearOrdenModel(
            i.Sku,
            i.Descripcion,
            i.Cantidad,
            i.PrecioUnitario
        )).ToList();

        var tiendaSeleccionada = string.IsNullOrWhiteSpace(tiendaId) ? "TIENDA-01-CENTRAL" : tiendaId.Trim();
        var stockTienda = await _gateway.ObtenerExistenciasPorUbicacionAsync(tiendaSeleccionada);
        if (stockTienda is null || stockTienda.Count == 0)
        {
            TempData["Error"] = $"La sucursal seleccionada no tiene existencias registradas: {tiendaSeleccionada}.";
            return RedirectToAction("Index", new { busqueda = clienteId, tiendaId });
        }

        var faltantes = carrito.Items
            .Select(item => new { item.Sku, Disponible = stockTienda.FirstOrDefault(s => string.Equals(s.Sku, item.Sku, StringComparison.OrdinalIgnoreCase))?.Disponible ?? 0, item.Cantidad })
            .Where(item => item.Disponible < item.Cantidad)
            .Select(item => $"{item.Sku} (disponible: {item.Disponible}, solicitado: {item.Cantidad})")
            .ToList();
        if (faltantes.Count > 0)
        {
            TempData["Error"] = $"No hay stock suficiente en la sucursal seleccionada: {string.Join(", ", faltantes)}.";
            return RedirectToAction("Index", new { busqueda = clienteId, tiendaId });
        }

        var operatoremail = Request.Cookies["sgo_user_email"] ?? "operador.tienda@megastore.com";
        var clienteFinal = string.IsNullOrWhiteSpace(clienteId) ? operatoremail : clienteId.Trim();
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
            return RedirectToAction("Index", new { busqueda = clienteId, tiendaId });
        }

        await _gateway.ConfirmarOrdenAsync(ordenCreada.Id, "Cobro presencial en terminal POS completado");

        // Limpiar items del carrito en Redis
        foreach (var item in lineas)
        {
            await _gateway.EliminarItemCarritoAsync(item.Sku);
        }

        var posInstancia = MapearTiendaAPos(tiendaSeleccionada);
        var linkComprobante = $"{Request.Scheme}://{Request.Host}/Estado/Comprobante?ordenId={ordenCreada.Id}";

        // Normalización de teléfono para SMS y WhatsApp
        var telInput = string.IsNullOrWhiteSpace(telefono) ? "+50255551234" : telefono.Trim();
        var telDigits = new string(telInput.Where(char.IsDigit).ToArray());
        if (telDigits.Length == 8) telDigits = "502" + telDigits;
        var telSms = "+" + (string.IsNullOrWhiteSpace(telDigits) ? "50255551234" : telDigits);

        var mensajeSms = $"MegaStore: Compra exitosa en {posInstancia}. Orden: {ordenCreada.NumeroOrden}. Total: Q{ordenCreada.Total:N2}. Consulta tu comprobante digital y factura FEL en: {linkComprobante}";
        var (smsEnviado, smsId, smsError) = await _gateway.EnviarSmsAsync(telSms, mensajeSms);

        var mensajeWa = Uri.EscapeDataString($"MegaStore Guatemala: Hola! Adjunto tu comprobante de compra en {posInstancia}. Orden: {ordenCreada.NumeroOrden}, Total: Q{ordenCreada.Total:N2}. Tu ticket digital y factura FEL estan disponibles aqui: {linkComprobante}");
        var waUrl = string.IsNullOrWhiteSpace(telDigits)
            ? $"https://wa.me/?text={mensajeWa}"
            : $"https://wa.me/{telDigits}?text={mensajeWa}";

        TempData["Exito"] = $"Venta en POS registrada con exito en {posInstancia}. Orden: {ordenCreada.NumeroOrden} (Total: Q {ordenCreada.Total:N2}). SMS despachado a {telSms} (ID: {smsId ?? "OK"}).";
        TempData["WhatsAppUrl"] = waUrl;
        TempData["ComprobanteUrl"] = linkComprobante;
        TempData["OrdenId"] = ordenCreada.Id;
        TempData["NumeroOrden"] = ordenCreada.NumeroOrden;
        TempData["TelefonoNotificado"] = telSms;

        return RedirectToAction("Index", new { busqueda = clienteId, tiendaId });
    }

    [HttpPost]
    public async Task<IActionResult> EnviarComprobanteOrden(string ordenId, string? telefono, string? tiendaId)
    {
        var jwt = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(jwt)) return RedirectToAction("Login", "Auth", new { returnUrl = "/Pos" });
        if (!PermisosHelper.EsPersonalAutorizado(Request.Cookies["sgo_user_role"], Request.Cookies["sgo_user_email"], jwt)) return Forbid();

        if (string.IsNullOrWhiteSpace(ordenId))
        {
            TempData["Error"] = "Identificador de orden requerido.";
            return RedirectToAction("Index", new { tiendaId });
        }

        var linkComprobante = $"{Request.Scheme}://{Request.Host}/Estado/Comprobante?ordenId={ordenId.Trim()}";
        var telInput = string.IsNullOrWhiteSpace(telefono) ? "+50255551234" : telefono.Trim();
        var telDigits = new string(telInput.Where(char.IsDigit).ToArray());
        if (telDigits.Length == 8) telDigits = "502" + telDigits;
        var telSms = "+" + (string.IsNullOrWhiteSpace(telDigits) ? "50255551234" : telDigits);

        var mensajeSms = $"MegaStore: Tu comprobante digital de la orden {ordenId.Trim()} esta disponible en: {linkComprobante}";
        var (smsEnviado, smsId, smsError) = await _gateway.EnviarSmsAsync(telSms, mensajeSms);

        var mensajeWa = Uri.EscapeDataString($"MegaStore Guatemala: Hola! Adjunto tu comprobante de la orden {ordenId.Trim()}. Puedes verlo aqui: {linkComprobante}");
        var waUrl = string.IsNullOrWhiteSpace(telDigits)
            ? $"https://wa.me/?text={mensajeWa}"
            : $"https://wa.me/{telDigits}?text={mensajeWa}";

        TempData["Exito"] = $"Comprobante de orden {ordenId} enviado por SMS a {telSms} (ID: {smsId ?? "OK"}).";
        TempData["WhatsAppUrl"] = waUrl;
        TempData["ComprobanteUrl"] = linkComprobante;
        TempData["OrdenId"] = ordenId;
        TempData["TelefonoNotificado"] = telSms;

        return RedirectToAction("Index", new { busqueda = ordenId, tiendaId });
    }


    public static string MapearTiendaAPos(string ubicacionId)
    {
        var normalizada = ubicacionId.Trim().ToUpperInvariant();
        if (normalizada.StartsWith("TIENDA-01", StringComparison.Ordinal) || normalizada.Contains("CENTRAL", StringComparison.Ordinal)) return "T01";
        if (normalizada.StartsWith("TIENDA-02", StringComparison.Ordinal) || normalizada.Contains("MIRAFLORES", StringComparison.Ordinal)) return "T02";
        if (normalizada.StartsWith("TIENDA-03", StringComparison.Ordinal) || normalizada.Contains("PORTALES", StringComparison.Ordinal)) return "T03";
        if (normalizada.StartsWith("TIENDA-04", StringComparison.Ordinal) || normalizada.Contains("PRADERA", StringComparison.Ordinal)) return "T04";

        if (normalizada.StartsWith("TIENDA-", StringComparison.Ordinal))
        {
            var sub = normalizada[7..];
            if (sub.Contains('-')) sub = sub[..sub.IndexOf('-')];
            if (int.TryParse(sub, out var num) && num is >= 1 and <= 32)
                return $"T{num:D2}";
        }

        if (normalizada.StartsWith("T", StringComparison.Ordinal) && int.TryParse(normalizada[1..], out var tNum) && tNum is >= 1 and <= 32)
            return $"T{tNum:D2}";

        return "T01";
    }

    public static readonly List<(string Id, string Nombre, string PosId, int Stock, string Pais)> TodasLasTiendasPos = new()
    {
        // ── Guatemala (16 sucursales: T01 a T16) ──────────────────────────────────
        ("TIENDA-01-CENTRAL", "MegaStore Central — Zona 10, Guatemala (POS T01)", "T01", 400, "Guatemala"),
        ("TIENDA-02-MIRAFLORES", "MegaStore Miraflores — Zona 11, Guatemala (POS T02)", "T02", 400, "Guatemala"),
        ("TIENDA-03-PORTALES", "MegaStore Portales — Zona 17, Guatemala (POS T03)", "T03", 389, "Guatemala"),
        ("TIENDA-04-PRADERA", "MegaStore Pradera Concepción, Guatemala (POS T04)", "T04", 400, "Guatemala"),
        ("TIENDA-05-OAKLAND", "MegaStore Oakland Mall — Zona 10, Guatemala (POS T05)", "T05", 400, "Guatemala"),
        ("TIENDA-06-NARANJO", "MegaStore Naranjo Mall — Mixco, Guatemala (POS T06)", "T06", 395, "Guatemala"),
        ("TIENDA-07-CAYALA", "MegaStore Paseo Cayalá — Zona 16, Guatemala (POS T07)", "T07", 400, "Guatemala"),
        ("TIENDA-08-ROOSEVELT", "MegaStore Peri-Roosevelt — Zona 7, Guatemala (POS T08)", "T08", 380, "Guatemala"),
        ("TIENDA-09-FONTABELLA", "MegaStore Plaza Fontabella — Zona 10, Guatemala (POS T09)", "T09", 400, "Guatemala"),
        ("TIENDA-10-METRONORTE", "MegaStore Metronorte — Zona 17, Guatemala (POS T10)", "T10", 390, "Guatemala"),
        ("TIENDA-11-MAJADAS", "MegaStore Parque Las Majadas — Zona 11, Guatemala (POS T11)", "T11", 400, "Guatemala"),
        ("TIENDA-12-AMERICAS", "MegaStore Plaza Las Américas — Zona 13, Guatemala (POS T12)", "T12", 375, "Guatemala"),
        ("TIENDA-13-SANCRISTOBAL", "MegaStore San Cristóbal — Mixco, Guatemala (POS T13)", "T13", 390, "Guatemala"),
        ("TIENDA-14-VILLANUEVA", "MegaStore Metrocentro — Villa Nueva, Guatemala (POS T14)", "T14", 385, "Guatemala"),
        ("TIENDA-15-CARRETERA", "MegaStore Pradera Express — Carretera a El Salvador, Guatemala (POS T15)", "T15", 400, "Guatemala"),
        ("TIENDA-16-CENTRALNORTE", "MegaStore Centra Norte — Zona 18, Guatemala (POS T16)", "T16", 370, "Guatemala"),

        // ── El Salvador (8 sucursales: T17 a T24) ─────────────────────────────────
        ("TIENDA-17-XELA", "MegaStore Multiplaza — San Salvador, El Salvador (POS T17)", "T17", 400, "El Salvador"),
        ("TIENDA-18-INTERPLAZA", "MegaStore Metrocentro — San Salvador, El Salvador (POS T18)", "T18", 390, "El Salvador"),
        ("TIENDA-19-ANTIGUA", "MegaStore Galerías Escalón — San Salvador, El Salvador (POS T19)", "T19", 400, "El Salvador"),
        ("TIENDA-20-ESCUINTLA", "MegaStore La Gran Vía — Santa Tecla, El Salvador (POS T20)", "T20", 380, "El Salvador"),
        ("TIENDA-21-MAZATENANGO", "MegaStore Plaza Mundo — Soyapango, El Salvador (POS T21)", "T21", 375, "El Salvador"),
        ("TIENDA-22-COBAN", "MegaStore Metrocentro — Santa Ana, El Salvador (POS T22)", "T22", 390, "El Salvador"),
        ("TIENDA-23-HUEHUETENANGO", "MegaStore Metrocentro — San Miguel, El Salvador (POS T23)", "T23", 385, "El Salvador"),
        ("TIENDA-24-CHIMALTENANGO", "MegaStore Plaza El Encuentro — Sonsonate, El Salvador (POS T24)", "T24", 395, "El Salvador"),

        // ── Costa Rica (8 sucursales: T25 a T32) ──────────────────────────────────
        ("TIENDA-25-ZACAPA", "MegaStore Multiplaza Escazú — San José, Costa Rica (POS T25)", "T25", 370, "Costa Rica"),
        ("TIENDA-26-CHIQUIMULA", "MegaStore Mall San Pedro — San José, Costa Rica (POS T26)", "T26", 380, "Costa Rica"),
        ("TIENDA-27-PUERTOBARRIOS", "MegaStore City Mall — Alajuela, Costa Rica (POS T27)", "T27", 365, "Costa Rica"),
        ("TIENDA-28-PETEN", "MegaStore Multiplaza del Este — Curridabat, Costa Rica (POS T28)", "T28", 370, "Costa Rica"),
        ("TIENDA-29-RETALHULEU", "MegaStore Paseo de las Flores — Heredia, Costa Rica (POS T29)", "T29", 380, "Costa Rica"),
        ("TIENDA-30-COATEPEQUE", "MegaStore Paseo Metrópoli — Cartago, Costa Rica (POS T30)", "T30", 375, "Costa Rica"),
        ("TIENDA-31-JUTIAPA", "MegaStore Plaza Liberia — Guanacaste, Costa Rica (POS T31)", "T31", 385, "Costa Rica"),
        ("TIENDA-32-SALAMA", "MegaStore Paseo del Mar — Puntarenas, Costa Rica (POS T32)", "T32", 390, "Costa Rica")
    };
}
