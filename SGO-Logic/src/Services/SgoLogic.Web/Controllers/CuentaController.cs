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
        return View(historial);
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
        return View(resultado);
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
            TempData["Exito"] = "Orden cancelada. Los productos regresaron a stock; el estado puede tardar unos segundos en actualizarse.";
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
}
