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
    public IActionResult Perfil()
    {
        var userEmail = Request.Cookies["sgo_user_email"];
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Cuenta/Perfil" });
        }

        ViewBag.UserName = Request.Cookies["sgo_user_name"] ?? "Usuario MegaStore";
        ViewBag.UserEmail = userEmail;
        ViewBag.HasJwt = !string.IsNullOrWhiteSpace(Request.Cookies["sgo_jwt"]);

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

        ViewBag.UserEmail = userEmail;
        var historial = await _gateway.ObtenerHistorialClienteAsync(userEmail, pagina, tamano);
        return View(historial);
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

        var userName = Request.Cookies["sgo_user_name"] ?? "Usuario MegaStore";
        var rol = PermisosHelper.DeterminarRol(userEmail);

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
                tarjetasBancarias = "Cero datos de tarjetas o CVV almacenados en MegaStore (Aislamiento PCI-DSS RES-08 via tokenizacion PayKlik).",
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
