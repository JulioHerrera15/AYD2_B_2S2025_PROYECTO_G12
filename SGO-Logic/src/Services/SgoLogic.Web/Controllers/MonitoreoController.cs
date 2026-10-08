using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public sealed record ServicioDefinicion(
    string Nombre,
    string InternalUrl,
    string PuertoPublico,
    string Categoria,
    string Rol,
    string Descripcion);

public sealed record ServicioHealthStatus(
    string Nombre,
    string Endpoint,
    string PuertoPublico,
    string Categoria,
    string Rol,
    string Estado,
    bool EstaEnLinea,
    long LatenciaMs,
    string Descripcion,
    DateTime VerificadoEn);

public sealed record SnapshotHistorialClienteModel(
    string CodigoCliente,
    string Email,
    DateTime ClienteDesde,
    int ComprasUltimos12Meses,
    decimal MontoCompradoUltimos12Meses,
    string Moneda,
    int PagosPuntuales,
    int PagosTardios,
    int MaxDiasAtraso,
    int Devoluciones,
    int IncidentesGraves,
    int OtrosIncidentes,
    DateTime FechaCorte,
    DateTime ActualizadoEn,
    string? LoteOrigen);

public sealed record RespuestaHistorialErpModel(IReadOnlyList<SnapshotHistorialClienteModel>? Clientes);

public class MonitoreoController : Controller
{
    private readonly GatewayClient _gateway;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly List<ServicioDefinicion> ServiciosEcosistema = new()
    {
        new("api-gateway", "http://api-gateway:8080/health/live", "8080", "Borde & Enrutamiento", "Enrutador Perimetral YARP", "Punto central de entrada y balanceo de solicitudes hacia los microservicios internos."),
        new("auth-api", "http://auth-api:8080/health/live", "8088", "Seguridad & Sesiones", "Identidad y Carrito Redis", "Gestion de cuentas de usuario, autenticacion Bearer, verificacion OTP y persistencia de carritos en memoria."),
        new("ordenes-api", "http://ordenes-api:8080/health/live", "8083", "Negocio Central", "Orquestador de Pedidos y Sagas", "Maquina de estados de compras omnicanal y coordinacion asincrona de pagos, existencias y facturacion."),
        new("inventario-api", "http://inventario-api:8080/health/live", "8094", "Negocio Central", "Existencias y Reservas", "Control de inventario unificado en centros de distribucion y las 32 tiendas fisicas con bloqueo optimista."),
        new("pagos-api", "http://pagos-api:8080/health/live", "8085", "Transacciones", "Pasarela de Cobros", "Procesamiento de pagos con tarjeta de credito y billetera digital con idempotencia garantizada."),
        new("credito-api", "http://credito-api:8080/health/live", "8099", "Transacciones", "Credito a 30 Dias", "Perfil crediticio precalculado a partir del historial del cliente y oferta de compra ahora, paga en 30 dias."),
        new("facturacion-api", "http://facturacion-api:8080/health/live", "8087", "Fiscal & Legal", "Facturacion SAT FEL", "Emision de documentos tributarios electronicos y contingencia ante indisponibilidad del certificador."),
        new("despacho-api", "http://despacho-api:8080/health/live", "8097", "Logistica", "Logistica y Despacho", "Gestion de guias de entrega a domicilio con LogiExpress y preparacion para recogida en tienda."),
        new("consulta-estado-api", "http://consulta-estado-api:8080/health/live", "8091", "Lectura & Trazabilidad", "Modelo de Lectura CQRS", "Consultas de estados de pedidos en tiempo real con proyecciones sincronizadas mediante colas de eventos."),
        new("notificaciones-api", "http://notificaciones-api:8080/health/live", "8090", "Comunicacion", "Avisos al Cliente", "Envio asincrono de notificaciones de compra, guias de despacho y alertas por correo electronico y mensajeria."),
        new("integracion-pos-worker", "http://integracion-pos-worker:8080/health/live", "Interno", "Integracion Tiendas", "Worker de Terminales POS", "Sincronizacion de eventos de venta en mostrador hacia las 32 terminales de tienda con reintentos y tolerancia a fallos."),
        new("integracion-erp-worker", "http://integracion-erp-worker:8080/health/live", "8096", "Integracion Core", "Worker de Consolidacion ERP", "Consolidacion de lotes de ventas hacia el sistema central AS/400 en formatos de intercambio CSV y XML."),
        new("auditoria-worker", "http://auditoria-worker:8080/health/live", "8098", "Seguridad & Compliance", "Bitacora Inmutable de Auditoria", "Registro transaccional append-only de eventos criticos en base de datos PostgreSQL con triggers de inmutabilidad."),
        new("mock-pos", "http://mock-pos:4001/", "4001", "Simulacion Tiendas", "Simulador 32 Terminales POS", "Simulador de las 32 tiendas fisicas para pruebas de eventos de venta y resiliencia ante desconexiones."),
        new("mock-erp", "http://mock-erp:4000/", "4000", "Simulacion Core", "Simulador ERP AS/400", "Simulador del sistema ERP corporativo para recepcion y confirmacion de lotes contables."),
        new("web-portal", "http://localhost:8080/health/live", "8092", "Presentacion", "Portal Web Omnicanal", "Frontend web para clientes y operadores con catalogo, POS en mostrador, rastreo y gestion de despacho.")
    };

    public MonitoreoController(GatewayClient gateway, IConfiguration config, IHttpClientFactory httpClientFactory)
    {
        _gateway = gateway;
        _config = config;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? clienteHistorial, CancellationToken ct)
    {
        var jwtToken = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(jwtToken))
        {
            TempData["Error"] = "Acceso restringido. Por favor inicia sesion con credenciales administrativas para consultar la consola de monitoreo.";
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Monitoreo" });
        }

        var userRole = Request.Cookies["sgo_user_role"];
        var userEmail = Request.Cookies["sgo_user_email"];
        if (!PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwtToken))
        {
            TempData["Error"] = "Acceso denegado. La consola de monitoreo requiere credenciales de operador o administrador de MegaStore.";
            return RedirectToAction("Index", "Home");
        }

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

        var exigirJwtConfig = _config["Gateway:ExigirJwt"] ?? "false";
        var exigirJwtActivo = exigirJwtConfig.Equals("true", StringComparison.OrdinalIgnoreCase);

        ViewBag.UserEmail = userEmail;
        ViewBag.UserRole = userRole ?? "OPERADOR";
        var esAdministrador = PermisosHelper.EsAdministrador(jwtToken, userRole, userEmail);
        ViewBag.EsAdministrador = esAdministrador;
        ViewBag.JwtExp = jwtExp;
        ViewBag.TokenValido = tokenValido;
        ViewBag.ExigirJwt = exigirJwtActivo ? "Activo (Estricto)" : "Modo Permisivo (Desarrollo)";

        // Realizar chequeos en tiempo real con HttpClient hacia los endpoints de salud
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(2);

        var tareas = ServiciosEcosistema.Select(s => ProbarServicioAsync(
            client, s.Nombre, s.InternalUrl, s.PuertoPublico, s.Categoria, s.Rol, s.Descripcion, HttpContext.RequestAborted
        ));

        var resultados = (await Task.WhenAll(tareas)).ToList();

        ViewBag.Servicios = resultados;
        ViewBag.TotalEnLinea = resultados.Count(r => r.EstaEnLinea);
        ViewBag.TotalServicios = resultados.Count;
        ViewBag.LatenciaPromedio = Math.Round(resultados.Where(r => r.EstaEnLinea).DefaultIfEmpty().Average(r => r?.LatenciaMs ?? 0), 1);
        ViewBag.UltimaActualizacion = DateTime.UtcNow;

        if (esAdministrador && !string.IsNullOrWhiteSpace(clienteHistorial))
        {
            ViewBag.EmailHistorialBuscado = clienteHistorial.Trim();
            await CargarSnapshotHistorialAsync(clienteHistorial.Trim(), ct);
        }

        return View();
    }

    private async Task CargarSnapshotHistorialAsync(string email, CancellationToken ct)
    {
        var baseUrl = _config["MockErp:BaseUrl"] ?? "http://mock-erp:4000";
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(4);

        try
        {
            using var response = await client.GetAsync(
                $"{baseUrl.TrimEnd('/')}/erp/clientes/historial",
                ct);
            if (!response.IsSuccessStatusCode)
            {
                ViewBag.ErrorSnapshotHistorial = $"El mock ERP respondio HTTP {(int)response.StatusCode}.";
                return;
            }

            var historial = await response.Content.ReadFromJsonAsync<RespuestaHistorialErpModel>(cancellationToken: ct);
            var snapshot = historial?.Clientes?.FirstOrDefault(cliente =>
                string.Equals(cliente.Email, email, StringComparison.OrdinalIgnoreCase));

            if (snapshot is null)
            {
                ViewBag.ErrorSnapshotHistorial = "No se encontro un snapshot para ese correo en el ERP.";
                return;
            }

            ViewBag.SnapshotHistorial = snapshot;
        }
        catch (HttpRequestException)
        {
            ViewBag.ErrorSnapshotHistorial = "No fue posible comunicarse con el mock ERP.";
        }
        catch (JsonException)
        {
            ViewBag.ErrorSnapshotHistorial = "El mock ERP devolvio un snapshot con formato invalido.";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            ViewBag.ErrorSnapshotHistorial = "El mock ERP no respondio dentro del tiempo esperado.";
        }
    }

    [HttpGet("api/monitoreo/estado")]
    public async Task<IActionResult> ObtenerEstadoEnVivo(CancellationToken ct)
    {
        var jwtToken = Request.Cookies["sgo_jwt"];
        var userRole = Request.Cookies["sgo_user_role"];
        var userEmail = Request.Cookies["sgo_user_email"];
        if (!PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwtToken))
        {
            return Forbid();
        }

        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(2);

        var tareas = ServiciosEcosistema.Select(s => ProbarServicioAsync(
            client, s.Nombre, s.InternalUrl, s.PuertoPublico, s.Categoria, s.Rol, s.Descripcion, ct
        ));

        var resultados = (await Task.WhenAll(tareas)).ToList();

        return Json(new
        {
            totalServicios = resultados.Count,
            totalEnLinea = resultados.Count(r => r.EstaEnLinea),
            latenciaPromedio = Math.Round(resultados.Where(r => r.EstaEnLinea).DefaultIfEmpty().Average(r => r?.LatenciaMs ?? 0), 1),
            ultimaActualizacion = DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm:ss UTC"),
            servicios = resultados
        });
    }

    private static async Task<ServicioHealthStatus> ProbarServicioAsync(
        HttpClient client,
        string nombre,
        string internalUrl,
        string puertoPublico,
        string categoria,
        string rol,
        string descripcion,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        bool enLinea = false;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(1500));
            var response = await client.GetAsync(internalUrl, cts.Token);
            sw.Stop();
            enLinea = response.IsSuccessStatusCode;
        }
        catch
        {
            sw.Stop();
            enLinea = false;
        }

        var estado = enLinea ? "OPERATIVO" : "NO DISPONIBLE";

        return new ServicioHealthStatus(
            Nombre: nombre,
            Endpoint: internalUrl,
            PuertoPublico: puertoPublico,
            Categoria: categoria,
            Rol: rol,
            Estado: estado,
            EstaEnLinea: enLinea,
            LatenciaMs: sw.ElapsedMilliseconds,
            Descripcion: descripcion,
            VerificadoEn: DateTime.UtcNow
        );
    }
}
