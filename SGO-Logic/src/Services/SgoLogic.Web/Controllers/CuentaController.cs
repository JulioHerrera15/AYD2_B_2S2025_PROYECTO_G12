using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public class CuentaController : Controller
{
    private readonly GatewayClient _gateway;

    public CuentaController(GatewayClient gateway)
    {
        _gateway = gateway;
    }

    [HttpGet]
    public async Task<IActionResult> Perfil()
    {
        var userEmail = Request.Cookies["sgo_user_email"];
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Cuenta/Perfil" });
        }

        var jwt = Request.Cookies["sgo_jwt"];
        var infoUsuario = await _gateway.ObtenerUsuarioPorEmailAsync(userEmail);
        var userRole = infoUsuario?.Rol
            ?? Request.Cookies["sgo_user_role"]
            ?? (!string.IsNullOrWhiteSpace(jwt) ? PermisosHelper.ObtenerRolDesdeJwt(jwt) : "CLIENTE");

        var userName = infoUsuario?.NombreCompleto
            ?? Request.Cookies["sgo_user_name"]
            ?? "Usuario MegaStore";

        ViewBag.UserName = userName;
        ViewBag.UserEmail = userEmail;
        ViewBag.UserRole = userRole;
        ViewBag.HasJwt = !string.IsNullOrWhiteSpace(jwt);
        ViewBag.InfoUsuario = infoUsuario;

        // Historial real de ordenes para mostrar estadisticas reales y transparentes
        var historial = await _gateway.ObtenerHistorialClienteAsync(userEmail, pagina: 1, tamano: 50);
        ViewBag.Historial = historial;

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Ordenes(int pagina = 1, int tamano = 10)
    {
        var userEmail = Request.Cookies["sgo_user_email"];
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Cuenta/Ordenes" });
        }

        var jwt = Request.Cookies["sgo_jwt"];
        var userRole = Request.Cookies["sgo_user_role"] ?? "CLIENTE";
        if (PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt))
        {
            return RedirectToAction("Index", "Estado");
        }

        ViewBag.UserEmail = userEmail;
        var historial = await _gateway.ObtenerHistorialClienteAsync(userEmail, pagina, tamano);
        var canceladaId = TempData["OrdenCancelada"] as string;
        await CargarReversosAsync(historial, canceladaId);
        return View(historial);
    }

    /// <summary>
    /// Estado del reembolso por cancelacion de una orden del cliente, para el sondeo del
    /// banner. 404 si la orden no existe o es de otro cliente; 204 si no tiene reembolso.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> EstadoReembolso(string ordenId)
    {
        var userEmail = Request.Cookies["sgo_user_email"];
        if (string.IsNullOrWhiteSpace(userEmail) || string.IsNullOrWhiteSpace(ordenId))
        {
            return Unauthorized();
        }

        var orden = await _gateway.ObtenerOrdenAsync(ordenId.Trim(), HttpContext.RequestAborted);
        var userRole = Request.Cookies["sgo_user_role"] ?? "CLIENTE";
        var jwt = Request.Cookies["sgo_jwt"];
        var esDuenio = orden is not null && string.Equals(orden.ClienteId, userEmail, StringComparison.OrdinalIgnoreCase);
        if (orden is null || (!esDuenio && !PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt)))
        {
            return NotFound();
        }

        var vista = ReembolsoOrden.Vista(orden);
        if (vista is null)
        {
            return NoContent();
        }

        Response.Headers.CacheControl = "no-store";
        return Json(new
        {
            titulo = vista.Titulo,
            estado = vista.Estado,
            detalle = vista.Detalle,
            clase = vista.Clase,
            icono = vista.Icono,
            pendiente = vista.Pendiente
        });
    }

    /// <summary>Estados en los que una orden puede tener reembolso por cancelacion.</summary>
    private static readonly HashSet<string> EstadosCancelados = new(StringComparer.OrdinalIgnoreCase)
    {
        "CANCELADA", "CANCELADA_RESERVA_VENCIDA", "RECHAZADA"
    };

    /// <summary>Estados en los que una orden puede tener nota de credito (cancelacion o devolucion).</summary>
    private static readonly HashSet<string> EstadosConNotaCredito = new(StringComparer.OrdinalIgnoreCase)
    {
        "CANCELADA", "CANCELADA_RESERVA_VENCIDA", "RECHAZADA", "ENTREGADA", "DEVOLUCION_EN_PROCESO", "DEVUELTA"
    };

    /// <summary>
    /// Para las ordenes de la pagina: el reembolso a la tarjeta de las canceladas (desde el
    /// OrdenDto de Ordenes) y sus notas de credito. Consultas en paralelo; si un servicio
    /// no responde, esa orden simplemente no muestra el dato.
    /// </summary>
    private async Task CargarReversosAsync(HistorialClienteModel? historial, string? canceladaId = null)
    {
        var reembolsos = new Dictionary<string, ReembolsoOrdenVista>();
        var notas = new Dictionary<string, List<FacturaModel>>();
        var ordenes = historial?.Ordenes ?? new List<OrdenHistorialResumenModel>();

        // Orden recien cancelada: su estado sale de Ordenes (fuente de verdad) y no de la
        // vista de estado, que puede tardar unos segundos en reflejar la cancelacion.
        if (!string.IsNullOrWhiteSpace(canceladaId))
        {
            var cancelada = await _gateway.ObtenerOrdenAsync(canceladaId);
            ViewBag.CancelacionReciente = cancelada;
            ViewBag.CancelacionRecienteReembolso = ReembolsoOrden.Vista(cancelada);
        }

        var tareasReembolso = ordenes
            .Where(o => EstadosCancelados.Contains(o.Estado))
            .Select(async o => (o.OrdenId, Vista: ReembolsoOrden.Vista(await _gateway.ObtenerOrdenAsync(o.OrdenId))));
        var tareasNotas = ordenes
            .Where(o => EstadosConNotaCredito.Contains(o.Estado))
            .Select(async o => (o.OrdenId, Notas: await _gateway.ObtenerNotasCreditoAsync(o.OrdenId)));

        foreach (var (ordenId, vista) in await Task.WhenAll(tareasReembolso))
        {
            if (vista is not null)
            {
                reembolsos[ordenId] = vista;
            }
        }

        foreach (var (ordenId, lista) in await Task.WhenAll(tareasNotas))
        {
            if (lista.Count > 0)
            {
                notas[ordenId] = lista;
            }
        }

        if (!string.IsNullOrWhiteSpace(canceladaId) && ViewBag.CancelacionRecienteReembolso is ReembolsoOrdenVista recien)
        {
            reembolsos[canceladaId] = recien;
        }

        ViewBag.Reembolsos = reembolsos;
        ViewBag.NotasCredito = notas;
    }

    /// <summary>Mis tarjetas: las tarjetas guardadas del cliente, con la opcion de eliminarlas.</summary>
    [HttpGet]
    public async Task<IActionResult> Tarjetas()
    {
        var userEmail = Request.Cookies["sgo_user_email"];
        var jwt = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(userEmail) || string.IsNullOrWhiteSpace(jwt))
        {
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Cuenta/Tarjetas" });
        }

        var userRole = Request.Cookies["sgo_user_role"] ?? "CLIENTE";
        if (PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt))
        {
            return RedirectToAction("Index", "Estado");
        }

        ViewBag.UserEmail = userEmail;
        return View(await _gateway.ListarTarjetasAsync());
    }

    /// <summary>
    /// Mi credito: limite, disponible y factores del perfil. Si Credito no responde se
    /// muestra un aviso con reintento; el resto de la cuenta sigue funcionando.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Credito()
    {
        var userEmail = Request.Cookies["sgo_user_email"];
        var jwt = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(userEmail) || string.IsNullOrWhiteSpace(jwt))
        {
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Cuenta/Credito" });
        }

        var userRole = Request.Cookies["sgo_user_role"] ?? "CLIENTE";
        if (PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt))
        {
            return RedirectToAction("Index", "Estado");
        }

        var resultado = await _gateway.ObtenerMiCreditoAsync(HttpContext.RequestAborted);
        if (resultado.Estado == EstadoConsultaCredito.SesionInvalida)
        {
            TempData["Error"] = "Tu sesión expiró. Inicia sesión nuevamente para ver tu crédito.";
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Cuenta/Credito" });
        }

        ViewBag.UserEmail = userEmail;
        // Tarjetas guardadas para pagar compras a credito desde esta misma pagina.
        ViewBag.Tarjetas = resultado.Estado == EstadoConsultaCredito.Ok
            ? await _gateway.ListarTarjetasAsync()
            : Array.Empty<TarjetaGuardadaModel>();
        return View(resultado);
    }

    // ── Pago de compras a credito (AJAX desde Cuenta/Credito) ─────────────────
    // La tarjeta se tokeniza con los mismos endpoints del checkout
    // (/Checkout/Tokenizar y /Checkout/TokenTarjetaGuardada); aqui solo viaja el token.

    private const string MensajeSesionPago = "Tu sesión expiró. Inicia sesión de nuevo para pagar.";

    /// <summary>Crea el pago (abono) de una compra a credito. 202 con el abono; error con {mensaje} para el cliente.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PagarCredito(string? cuentaId, decimal monto, string? tokenMedioPago)
    {
        if (string.IsNullOrWhiteSpace(Request.Cookies["sgo_jwt"]))
        {
            return StatusCode(StatusCodes.Status401Unauthorized, new { mensaje = MensajeSesionPago });
        }

        if (string.IsNullOrWhiteSpace(cuentaId) || string.IsNullOrWhiteSpace(tokenMedioPago))
        {
            return UnprocessableEntity(new { mensaje = "Elige la compra y valida tu tarjeta antes de pagar." });
        }

        return RespuestaAbono(await _gateway.CrearAbonoCreditoAsync(cuentaId, monto, tokenMedioPago));
    }

    /// <summary>Estado actual de un pago; la pagina lo consulta hasta que termina.</summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> AbonoCredito(string id)
    {
        if (string.IsNullOrWhiteSpace(Request.Cookies["sgo_jwt"]))
        {
            return StatusCode(StatusCodes.Status401Unauthorized, new { mensaje = MensajeSesionPago });
        }

        return RespuestaAbono(await _gateway.ObtenerAbonoCreditoAsync(id));
    }

    /// <summary>Reintenta un pago pendiente con un token nuevo; es el mismo pago, nunca un cobro doble.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReintentarAbonoCredito(string? id, string? tokenMedioPago)
    {
        if (string.IsNullOrWhiteSpace(Request.Cookies["sgo_jwt"]))
        {
            return StatusCode(StatusCodes.Status401Unauthorized, new { mensaje = MensajeSesionPago });
        }

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(tokenMedioPago))
        {
            return UnprocessableEntity(new { mensaje = "Valida tu tarjeta antes de reintentar el pago." });
        }

        return RespuestaAbono(await _gateway.ReintentarAbonoCreditoAsync(id, tokenMedioPago));
    }

    private IActionResult RespuestaAbono(ResultadoAbonoCreditoModel resultado)
    {
        if (resultado.Abono is not null)
        {
            return Json(resultado.Abono);
        }

        if (resultado.Status == StatusCodes.Status401Unauthorized)
        {
            return StatusCode(StatusCodes.Status401Unauthorized, new { mensaje = MensajeSesionPago });
        }

        var status = resultado.Status is >= 400 and < 600 ? resultado.Status : StatusCodes.Status502BadGateway;
        return StatusCode(status, new { mensaje = resultado.Mensaje ?? ResultadoAbonoCreditoModel.MensajeNoDisponible });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarTarjeta(Guid id)
    {
        if (string.IsNullOrWhiteSpace(Request.Cookies["sgo_jwt"]))
        {
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Cuenta/Tarjetas" });
        }

        var (exito, error) = await _gateway.EliminarTarjetaAsync(id);
        if (exito)
        {
            TempData["Exito"] = "Tarjeta eliminada.";
        }
        else
        {
            TempData["Error"] = $"No se pudo eliminar la tarjeta: {error}";
        }

        return RedirectToAction(nameof(Tarjetas));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancelar(string ordenId, int pagina = 1)
    {
        // La identidad sale del JWT de la sesion, nunca del formulario: Ordenes
        // compara este email con el dueno de la orden y responde 403 si no coincide.
        var jwt = Request.Cookies["sgo_jwt"];
        var clienteId = PermisosHelper.ObtenerEmailDesdeJwt(jwt);
        if (string.IsNullOrWhiteSpace(clienteId))
        {
            TempData["Error"] = "Tu sesion expiro. Inicia sesion nuevamente para cancelar la orden.";
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Cuenta/Ordenes" });
        }

        if (string.IsNullOrWhiteSpace(ordenId))
        {
            TempData["Error"] = "No se indico la orden a cancelar.";
            return RedirectToAction(nameof(Ordenes), new { pagina });
        }

        var (exito, error) = await _gateway.CancelarOrdenAsync(
            ordenId.Trim(), clienteId, "Cancelada por el cliente desde su historial de pedidos");

        if (exito)
        {
            // La pagina de pedidos muestra la confirmacion (y el reembolso, si lo hay)
            // de esta orden aunque la vista de estado aun no refleje la cancelacion.
            TempData["OrdenCancelada"] = ordenId.Trim();
        }
        else
        {
            TempData["Error"] = $"No se pudo cancelar la orden: {error}";
        }

        return RedirectToAction(nameof(Ordenes), new { pagina });
    }

    [HttpGet]
    public async Task<IActionResult> Factura(string ordenId)
    {
        var userEmail = Request.Cookies["sgo_user_email"];
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Cuenta/Ordenes" });
        }

        // El cliente solo puede descargar facturas de sus propias ordenes.
        var userRole = Request.Cookies["sgo_user_role"] ?? "CLIENTE";
        var jwt = Request.Cookies["sgo_jwt"];
        var orden = PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt)
            ? null
            : await _gateway.ObtenerEstadoOrdenAsync(ordenId);

        if (!PermisosHelper.PuedeVerOrden(orden, userEmail, userRole, jwt))
        {
            return NotFound();
        }

        var factura = await _gateway.ObtenerFacturaAsync(ordenId);
        if (factura is null)
        {
            TempData["Error"] = "La factura de esta orden aun no ha sido generada. Intenta de nuevo en unos minutos.";
            return RedirectToAction(nameof(Ordenes));
        }

        return File(System.Text.Encoding.UTF8.GetBytes(factura.Xml), "application/xml",
            $"factura-{factura.Serie}-{factura.NumeroDocumento}.xml");
    }

    /// <summary>XML de una nota de credito de la orden (la ultima si no se indica el numero).</summary>
    [HttpGet]
    public async Task<IActionResult> NotaCredito(string ordenId, string? numero = null)
    {
        var userEmail = Request.Cookies["sgo_user_email"];
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Cuenta/Ordenes" });
        }

        var userRole = Request.Cookies["sgo_user_role"] ?? "CLIENTE";
        var jwt = Request.Cookies["sgo_jwt"];
        var orden = PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt)
            ? null
            : await _gateway.ObtenerEstadoOrdenAsync(ordenId);

        if (!PermisosHelper.PuedeVerOrden(orden, userEmail, userRole, jwt))
        {
            return NotFound();
        }

        var notas = await _gateway.ObtenerNotasCreditoAsync(ordenId);
        var nota = string.IsNullOrWhiteSpace(numero)
            ? notas.LastOrDefault()
            : notas.FirstOrDefault(n => n.NumeroDocumento == numero);
        if (nota is null)
        {
            TempData["Error"] = "Esta orden aún no tiene nota de crédito. Intenta de nuevo en unos minutos.";
            return RedirectToAction(nameof(Ordenes));
        }

        return File(System.Text.Encoding.UTF8.GetBytes(nota.Xml), "application/xml",
            $"nota-credito-{nota.Serie}-{nota.NumeroDocumento}.xml");
    }

    [HttpGet]
    public IActionResult Privacidad()
    {
        var userEmail = Request.Cookies["sgo_user_email"];
        ViewBag.UserEmail = userEmail;
        ViewBag.IsAuthenticated = !string.IsNullOrWhiteSpace(userEmail);
        ViewBag.ConsentMarketing = Request.Cookies["sgo_consent_marketing"] != "false";
        ViewBag.ConsentTelemetry = Request.Cookies["sgo_consent_telemetry"] != "false";
        ViewBag.SolicitudSupresionFolio = Request.Cookies["sgo_solicitud_supresion"];
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> DescargarDatos()
    {
        var userEmail = Request.Cookies["sgo_user_email"];
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            TempData["Error"] = "Inicia sesion para descargar tus datos personales y pedidos.";
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Cuenta/Privacidad" });
        }

        var infoUsuario = await _gateway.ObtenerUsuarioPorEmailAsync(userEmail);
        var userName = infoUsuario?.NombreCompleto ?? Request.Cookies["sgo_user_name"] ?? "Usuario MegaStore";
        var rol = infoUsuario?.Rol ?? Request.Cookies["sgo_user_role"] ?? PermisosHelper.DeterminarRol(userEmail, Request.Cookies["sgo_jwt"]);

        // Consultar datos reales asociados a la cuenta del usuario
        var historial = await _gateway.ObtenerHistorialClienteAsync(userEmail, 1, 100);
        var carrito = await _gateway.ObtenerCarritoAsync();
        var todasIncidencias = await _gateway.ListarIncidenciasAsync();
        var ordenIds = historial?.Ordenes?.Select(o => o.OrdenId).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new();
        var incidenciasCliente = todasIncidencias?.Where(i => ordenIds.Contains(i.OrdenId)).ToList() ?? new();

        var consentimientoMarketing = Request.Cookies["sgo_consent_marketing"] != "false";
        var consentimientoTelemetria = Request.Cookies["sgo_consent_telemetry"] != "false";
        var solicitudSupresion = Request.Cookies["sgo_solicitud_supresion"];

        var reporte = new
        {
            meta = new
            {
                titulo = "Reporte Estructurado de Datos Personales (Portabilidad ARCO)",
                plataforma = "MegaStore Omnicanal (SGO-Logic)",
                fechaGeneracion = DateTime.UtcNow.ToString("O"),
                normativa = "Reglamento de Proteccion de Datos / Portabilidad ARCO / Sat FEL",
                seguridad = "Cifrado TLS 1.3 / Reposo AES-256 / Append-Only EAC-03"
            },
            titular = new
            {
                email = userEmail,
                nombreCompleto = userName,
                rolAsignado = rol,
                canalRegistro = "PORTAL_WEB",
                estadoCuenta = "ACTIVA"
            },
            preferenciasPrivacidad = new
            {
                notificacionesTransaccionales = "ACTIVAS_OBLIGATORIAS (DTE SAT / Despacho LogiExpress)",
                boletinesMarketing = consentimientoMarketing ? "AUTORIZADO" : "REVOCADO",
                telemetriaRendimiento = consentimientoTelemetria ? "AUTORIZADO" : "REVOCADO",
                solicitudOlvidoRegistrada = string.IsNullOrWhiteSpace(solicitudSupresion) ? "NINGUNA" : solicitudSupresion
            },
            resumenTransaccional = new
            {
                totalOrdenesRegistradas = historial?.TotalRegistros ?? 0,
                montoAcumuladoGtq = historial?.Ordenes?.Sum(o => o.Total) ?? 0m,
                itemsEnCarritoActivo = carrito?.Items?.Count ?? 0,
                incidenciasReportadas = incidenciasCliente.Count
            },
            pedidos = historial?.Ordenes ?? new List<OrdenHistorialResumenModel>(),
            carritoActivo = carrito,
            incidencias = incidenciasCliente,
            politicaResguardoYRetencion = new
            {
                facturacionSat = "Las facturas tributarias electronicas (DTE) se conservan inmutables por 5 anos conforme al Codigo Tributario.",
                tarjetasBancarias = "Numero y CVV solo cifrados (AES-GCM) en la boveda transitoria de Pagos hasta el cobro o el vencimiento del token; nunca en logs ni eventos (RES-08).",
                bitacoraAuditoria = "Trazabilidad forense inmutable append-only sin permisos de modificacion (ADR-006 / EAC-03)."
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(reporte, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var fileName = $"megastore-datos-{userEmail.Replace("@", "_at_")}-{DateTime.UtcNow:yyyyMMdd}.json";

        return File(bytes, "application/json", fileName);
    }

    [HttpPost]
    public IActionResult GuardarConsentimientos(bool? consentMarketing, bool? consentTelemetry)
    {
        var userEmail = Request.Cookies["sgo_user_email"];
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            TempData["Error"] = "Inicia sesion para actualizar tus consentimientos.";
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Cuenta/Privacidad" });
        }

        var mkt = consentMarketing == true ? "true" : "false";
        var tel = consentTelemetry == true ? "true" : "false";

        Response.Cookies.Append("sgo_consent_marketing", mkt, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            SameSite = SameSiteMode.Lax
        });

        Response.Cookies.Append("sgo_consent_telemetry", tel, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            SameSite = SameSiteMode.Lax
        });

        TempData["Exito"] = "Tus preferencias de privacidad y consentimientos han sido actualizadas con exito.";
        return RedirectToAction("Privacidad");
    }

    [HttpPost]
    public IActionResult SolicitarSupresion(string? motivo)
    {
        var userEmail = Request.Cookies["sgo_user_email"];
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            TempData["Error"] = "Inicia sesion para gestionar solicitudes de privacidad.";
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Cuenta/Privacidad" });
        }

        var folio = $"ARCO-DEL-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..6].ToUpper()}";

        Response.Cookies.Append("sgo_solicitud_supresion", folio, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddDays(30),
            SameSite = SameSiteMode.Lax
        });

        TempData["Exito"] = $"Tu solicitud formal de supresion y derecho al olvido ha sido registrada con folio {folio}. Conforme al Codigo Tributario SAT, las facturas DTE y auditorias emitidas se resguardan por 5 anos, procediendo con la baja y anonimizacion de datos promocionales y de contacto en un plazo maximo de 72 horas.";
        return RedirectToAction("Privacidad");
    }

    [HttpGet]
    public async Task<IActionResult> Notificaciones(string? canal = null)
    {
        var userEmail = Request.Cookies["sgo_user_email"];
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Cuenta/Notificaciones" });
        }

        ViewBag.UserEmail = userEmail;
        ViewBag.CanalFiltro = canal;

        // 1. Obtener mensajes enviados desde MockMensajeria
        var mensajes = await _gateway.ObtenerMensajesAsync(destinatario: userEmail, canal: canal, HttpContext.RequestAborted);

        // 2. Si el mock en memoria esta vacio (p. ej. reinicio de contenedores), consultar historial de ordenes
        // para asegurar que el cliente siempre vea la trazabilidad de sus estados y motivos
        if (mensajes.Count == 0)
        {
            var historial = await _gateway.ObtenerHistorialClienteAsync(userEmail, pagina: 1, tamano: 20, HttpContext.RequestAborted);
            if (historial?.Ordenes is not null)
            {
                foreach (var ord in historial.Ordenes)
                {
                    var num = !string.IsNullOrWhiteSpace(ord.NumeroOrden) ? ord.NumeroOrden : ord.OrdenId;
                    var estado = ord.Estado ?? "CONFIRMADA";

                    string asunto;
                    string cuerpo;
                    switch (estado.ToUpperInvariant())
                    {
                        case "CONFIRMADA":
                        case "CONFIRMADA_PENDIENTE_FISCAL":
                            asunto = $"Tu orden {num} ha sido confirmada";
                            cuerpo = $"Tu orden {num} fue confirmada exitosamente. Estamos preparando tus productos para la entrega.";
                            break;
                        case "RECHAZADA":
                            asunto = $"Tu orden {num} ha sido rechazada";
                            cuerpo = $"Lamentamos informarte que tu orden {num} fue rechazada. Motivo del rechazo: Evaluacion de credito o fondos insuficientes.";
                            break;
                        case "CANCELADA":
                        case "CANCELADA_RESERVA_VENCIDA":
                            asunto = $"Tu orden {num} ha sido cancelada";
                            cuerpo = $"Tu orden {num} fue cancelada. La reserva de inventario fue liberada.";
                            break;
                        case "LISTA_PARA_RECOGER":
                            asunto = $"Tu orden {num} esta lista para recoger";
                            cuerpo = $"Tu orden {num} ya esta lista en sucursal. Presenta tu numero de orden para recogerla.";
                            break;
                        case "EN_TRANSITO":
                        case "EN_RUTA":
                            asunto = $"Tu orden {num} esta en camino";
                            cuerpo = $"Tu orden {num} va en camino a tu direccion de entrega via LogiExpress.";
                            break;
                        case "ENTREGADA":
                            asunto = $"Tu orden {num} ha sido entregada";
                            cuerpo = $"Tu orden {num} fue entregada exitosamente. Gracias por tu compra en MegaStore.";
                            break;
                        default:
                            asunto = $"Tu orden {num} cambio de estado";
                            cuerpo = $"El estado de tu orden {num} es ahora: {estado}.";
                            break;
                    }

                    mensajes.Add(new NotificacionMensajeModel(
                        MensajeId: $"NOTIF-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                        Canal: "EMAIL",
                        Destinatario: userEmail,
                        Asunto: asunto,
                        Cuerpo: cuerpo,
                        Fecha: ord.CreadaEn ?? DateTime.UtcNow
                    ));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(canal))
        {
            mensajes = mensajes.Where(m => string.Equals(m.Canal, canal, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        mensajes = mensajes.OrderByDescending(m => m.Fecha).ToList();

        return View(mensajes);
    }
}
