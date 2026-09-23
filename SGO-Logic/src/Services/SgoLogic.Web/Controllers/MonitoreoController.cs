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

public sealed record TarjetaPrueba(
    string Numero,
    string Vencimiento,
    string Cvv,
    string Titular,
    string Franquicia,
    string Escenario,
    string ResultadoEsperado,
    string ColorBadge);

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

        var tarjetas = new List<TarjetaPrueba>
        {
            new("4111 1111 1111 1111", "12/28", "123", "Juan Perez", "Visa", "Camino Feliz (Aprobado)", "Token emitido y cobro completado exitosamente", "bg-emerald-100 text-emerald-800 border-emerald-300"),
            new("5555 5555 5555 4444", "06/27", "456", "Maria Lopez", "MasterCard", "Camino Feliz Alternativo", "Token emitido y cobro completado", "bg-emerald-100 text-emerald-800 border-emerald-300"),
            new("4000 0000 0000 0002", "11/26", "789", "Carlos Gomez", "Visa", "Fondos Insuficientes", "Rechazo de pasarela; orden queda en PAGO_PENDIENTE", "bg-amber-100 text-amber-800 border-amber-300"),
            new("4000 0000 0000 9999", "01/24", "111", "Ana Rodriguez", "Visa", "Tarjeta Expirada", "Rechazo inmediato de validación en cliente o PayKlik", "bg-rose-100 text-rose-800 border-rose-300"),
            new("4000 0000 0000 0005", "08/29", "999", "Roberto Morales", "Visa", "Bloqueo por Fraude", "Rechazo de seguridad y registro en bitácora de auditoría", "bg-red-100 text-red-800 border-red-300")
        };

        ViewBag.Servicios = servicios;
        ViewBag.Tarjetas = tarjetas;

        return View();
    }
}
