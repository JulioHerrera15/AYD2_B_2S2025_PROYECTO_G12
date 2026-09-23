using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public sealed record ServicioHealthStatus(
    string Nombre,
    string Endpoint,
    string Rol,
    string Estado,
    string Descripcion);

public class MonitoreoController : Controller
{
    private readonly GatewayClient _gateway;
    private readonly IConfiguration _config;

    public MonitoreoController(GatewayClient gateway, IConfiguration config)
    {
        _gateway = gateway;
        _config = config;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var jwtToken = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(jwtToken))
        {
            TempData["Error"] = "Acceso restringido. Por favor inicia sesión con credenciales administrativas para consultar la consola de monitoreo.";
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Monitoreo" });
        }

        string? jwtSubject = null;
        string? jwtEmail = null;
        DateTime? jwtExp = null;
        bool tokenValido = false;

        if (!string.IsNullOrWhiteSpace(jwtToken) && jwtToken.Contains('.'))
        {
            try
            {
                var parts = jwtToken.Split('.');
                if (parts.Length >= 2)
                {
                    var base64 = parts[1].Replace('-', '+').Replace('_', '/');
                    switch (base64.Length % 4)
                    {
                        case 2: base64 += "=="; break;
                        case 3: base64 += "="; break;
                    }
                    var jsonBytes = Convert.FromBase64String(base64);
                    using var doc = JsonDocument.Parse(jsonBytes);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("sub", out var subProp)) jwtSubject = subProp.GetString();
                    if (root.TryGetProperty("email", out var emailProp)) jwtEmail = emailProp.GetString();
                    if (root.TryGetProperty("exp", out var expProp) && expProp.TryGetInt64(out var expSeconds))
                    {
                        jwtExp = DateTimeOffset.FromUnixTimeSeconds(expSeconds).UtcDateTime;
                        tokenValido = jwtExp > DateTime.UtcNow;
                    }
                }
            }
            catch
            {
                tokenValido = false;
            }
        }

        ViewBag.JwtToken = jwtToken;
        ViewBag.JwtSubject = jwtSubject;
        ViewBag.JwtEmail = jwtEmail;
        ViewBag.JwtExp = jwtExp;
        ViewBag.TokenValido = tokenValido;
        ViewBag.ExigirJwt = _config["Gateway:ExigirJwt"] ?? "false (default per ADR-020)";

        var servicios = new List<ServicioHealthStatus>
        {
            new("api-gateway", "http://localhost:8080", "Borde & Enrutador YARP", "Operativo", "Punto único de entrada y validación de borde (ADR-020)"),
            new("auth-api", "http://localhost:8088", "Identidad y Carrito", "Operativo", "Autenticación JWT, verificación 2FA y carrito en Redis (HU-007)"),
            new("ordenes-api", "http://localhost:8083", "Orquestador de Saga", "Operativo", "Máquina de estados de la orden y saga centralizada (ADR-002)"),
            new("inventario-api", "http://localhost:8094", "Gestión de Existencias", "Operativo", "Bloqueo optimista y reservas unificadas en 32 tiendas (ADR-005)"),
            new("pagos-api", "http://localhost:8085", "Pasarela de Cobros", "Operativo", "Procesamiento de pagos con tarjeta y transferencias (ADR-010)"),
            new("facturacion-api", "http://localhost:8087", "Facturación SAT FEL", "Operativo", "Emisión asíncrona de DTEs y contingencia fiscal (ADR-011, ADR-015)"),
            new("despacho-api", "http://localhost:8097", "Logística y Envíos", "Operativo", "Despacho a domicilio con LogiExpress y Click & Collect (ADR-017)"),
            new("consulta-estado-api", "http://localhost:8091", "Modelo de Lectura CQRS", "Operativo", "Consultas en caliente desde Redis y PostgreSQL < 5s (ADR-004)"),
            new("notificaciones-api", "http://localhost:8090", "Avisos al Cliente", "Operativo", "Notificaciones de estado por correo y SMS (HU-020)"),
            new("auditoria-worker", "Postgres :5435", "Bitácora Inmutable", "Operativo", "Append-only con permisos restringidos y triggers (ADR-006, EAC-03)"),
            new("web-portal", "http://localhost:8092", "Portal Web Omnicanal", "Operativo", "Frontend ASP.NET Core MVC con diseño responsive y alta fidelidad")
        };

        ViewBag.Servicios = servicios;

        return View();
    }
}

