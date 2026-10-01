using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using SgoLogic.BuildingBlocks.Serialization;

namespace SgoLogic.Web.Services;

public sealed record RegistroModel(string Email, string Contrasena, string NombreCompleto);
public sealed record VerificarModel(string Email, string Codigo);
public sealed record LoginModel(string Email, string Contrasena);
public sealed record AuthResult(
    string Token,
    Guid UsuarioId,
    string Email,
    string NombreCompleto,
    DateTime ExpiraEn,
    [property: JsonPropertyName("rol")] string? Rol = "CLIENTE");

public sealed record UsuarioDto(
    Guid Id,
    string Email,
    string NombreCompleto,
    string Rol,
    bool EmailVerificado,
    DateTime CreadoEn);

public sealed record ItemCarritoModel(
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("descripcion")] string Descripcion,
    [property: JsonPropertyName("cantidad")] int Cantidad,
    [property: JsonPropertyName("precioUnitario")] decimal PrecioUnitario)
{
    [JsonIgnore]
    public decimal Subtotal => Cantidad * PrecioUnitario;
}

public sealed record CarritoModel(
    [property: JsonPropertyName("clienteOSesionId")] string? ClienteOSesionId = null,
    [property: JsonPropertyName("items")] List<ItemCarritoModel>? Items = null,
    [property: JsonPropertyName("total")] decimal Total = 0m)
{
    [JsonIgnore]
    public string CarritoId => ClienteOSesionId ?? string.Empty;
}

public sealed record AgregarItemModel(string Sku, string Descripcion, int Cantidad, decimal PrecioUnitario);

public sealed record IncidenciaModel(Guid Id, string OrdenId, string Descripcion, string Estado, string? Resolucion, DateTime CreadaEn);
public sealed record OrdenEstadoModel(
    string OrdenId,
    string? NumeroOrden,
    string? Estado,
    string? Canal,
    string? MetodoEntrega,
    decimal? Total,
    string? Moneda,
    string? PagoId,
    string? DocumentoFiscalId,
    DateTime? CreadaEn,
    DateTime? ActualizadaEn,
    List<IncidenciaModel>? Incidencias,
    string? MotivoUltimoCambio = null,
    string? ClienteId = null);

public sealed record CrearIncidenciaModel(string OrdenId, string Descripcion);

// ── Modelos de Órdenes y Checkout ───────────────────────────────────────────
public sealed record LineaCrearOrdenModel(string Sku, string Descripcion, int Cantidad, decimal PrecioUnitario);

public sealed record DireccionEntregaModel(
    [property: JsonPropertyName("pais")] string Pais,
    [property: JsonPropertyName("departamento")] string Departamento,
    [property: JsonPropertyName("municipio")] string Municipio,
    [property: JsonPropertyName("direccion")] string Direccion,
    [property: JsonPropertyName("referencia")] string? Referencia = null);

public sealed record CrearOrdenModel(
    string ClienteId,
    string Canal,
    string MetodoEntrega,
    string Moneda,
    string MedioPago,
    decimal Impuestos,
    List<LineaCrearOrdenModel> Lineas,
    string? UbicacionSolicitadaId = null,
    string? ReceptorCui = null,
    DireccionEntregaModel? DireccionEntrega = null,
    string? TokenMedioPago = null);

public sealed record OrdenCreadaModel(
    string Id,
    string NumeroOrden,
    string ClienteId,
    string Canal,
    string MetodoEntrega,
    string Estado,
    decimal Total,
    DateTime CreadaEn);

public sealed record ConfirmarOrdenResponse(
    OrdenCreadaModel Orden,
    bool YaConfirmada);

public sealed record OrdenHistorialResumenModel(
    string OrdenId,
    string NumeroOrden,
    string Estado,
    decimal Total,
    DateTime ActualizadaEn,
    string? ClienteId = null,
    DateTime? CreadaEn = null);

public sealed record FacturaModel(string Serie, string NumeroDocumento, string Xml);

// ── Informe de ventas conciliado (Integracion ERP) ───────────────────────────
public sealed record InformeVentasModel(DateTime GeneradoEn, int VentasPendientes, decimal MontoPendiente, List<LoteInformeModel> Lotes);
public sealed record LoteInformeModel(
    string LoteId, long Secuencia, DateTime FechaGeneracion, string Formato, string Estado,
    int CantidadRegistros, decimal MontoTotal, int Intentos, int? CodigoHttp, DateTime? EnviadoEn, DateTime? ConfirmadoEn,
    ConciliacionInformeModel? Conciliacion, List<RegistroInformeModel> Registros);
public sealed record ConciliacionInformeModel(DateTime Fecha, int RegistrosErp, decimal MontoErp, bool Conciliada, string? Diferencias, DateTime CreadaEn);
public sealed record RegistroInformeModel(string OrdenId, string Canal, string Tienda, decimal Monto, DateTime Fecha);
public sealed record EventoAuditoriaModel(string EventId, string EventType, DateTime OccurredAt, string TransactionId, string AggregateId, string Source, DateTime FechaRecepcion);
public sealed record PaginaAuditoriaModel(List<EventoAuditoriaModel> Items, int Total);

public sealed record HistorialClienteModel(
    string ClienteId,
    int Pagina,
    int Tamano,
    int TotalRegistros,
    List<OrdenHistorialResumenModel> Ordenes);

// ── Modelos de Reintento de Pago ─────────────────────────────────────────────
public sealed record EnlaceReintentoInfoModel(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("ordenId")] string OrdenId,
    [property: JsonPropertyName("monto")] decimal Monto,
    [property: JsonPropertyName("moneda")] string Moneda,
    [property: JsonPropertyName("estaVigente")] bool EstaVigente,
    [property: JsonPropertyName("usado")] bool Usado,
    [property: JsonPropertyName("expiraEn")] DateTime ExpiraEn,
    [property: JsonPropertyName("estadoPago")] string EstadoPago,
    [property: JsonPropertyName("ultimosCuatro")] string? UltimosCuatro,
    [property: JsonPropertyName("marca")] string? Marca,
    [property: JsonPropertyName("clienteId")] string? ClienteId);

public sealed record IntentoPagoResultadoModel(
    [property: JsonPropertyName("intentoNumero")] int IntentoNumero,
    [property: JsonPropertyName("codigoRespuesta")] string? CodigoRespuesta,
    [property: JsonPropertyName("mensajeRespuesta")] string? MensajeRespuesta);

public sealed record PagoReintentoResultadoModel(
    [property: JsonPropertyName("ordenId")] string? OrdenId,
    [property: JsonPropertyName("estado")] string? Estado,
    [property: JsonPropertyName("monto")] decimal? Monto,
    [property: JsonPropertyName("moneda")] string? Moneda,
    [property: JsonPropertyName("intentos")] List<IntentoPagoResultadoModel>? Intentos = null);

// ── Modelos de Despacho ─────────────────────────────────────────────────────
public sealed record DespachoOrdenModel(
    [property: JsonPropertyName("id")] string? DespachoId,
    string OrdenId,
    string Estado,
    string MetodoEntrega,
    string? GuiaId,
    string? TrackingUrl,
    string? Transportista,
    string? UbicacionOrigenId,
    [property: JsonPropertyName("creadoEn")] DateTime? CreadoEn = null,
    [property: JsonPropertyName("entregadoEn")] DateTime? EntregadoEn = null,
    [property: JsonPropertyName("detalleEstado")] string? DetalleEstado = null);

public sealed record DespachoPreparacionDto(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("ordenId")] string OrdenId,
    [property: JsonPropertyName("ubicacionOrigenId")] string? UbicacionOrigenId,
    [property: JsonPropertyName("metodoEntrega")] string MetodoEntrega,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("guiaId")] string? GuiaId,
    [property: JsonPropertyName("creadoEn")] DateTime? CreadoEn)
{
    [JsonPropertyName("despachoId")]
    public string DespachoId => !string.IsNullOrWhiteSpace(Id) ? Id : (!string.IsNullOrWhiteSpace(OrdenId) ? OrdenId : string.Empty);
}

// ── Modelos de Inventario ───────────────────────────────────────────────────
public sealed record ExistenciaSkuDto(
    string Sku,
    string UbicacionId,
    int CantidadFisica,
    int CantidadReservada,
    [property: JsonPropertyName("disponible")] int Disponible,
    string? TipoUbicacion = null,
    int Version = 0)
{
    [JsonPropertyName("cantidadDisponible")]
    public int CantidadDisponible => Disponible;
}

public sealed record UbicacionResumenDto(
    string UbicacionId,
    string TipoUbicacion,
    [property: JsonPropertyName("cantidadDeSkus")] int TotalSkus,
    [property: JsonPropertyName("cantidadTotalFisica")] int CantidadFisicaTotal,
    [property: JsonPropertyName("cantidadTotalReservada")] int CantidadReservadaTotal,
    [property: JsonPropertyName("totalDisponible")] int CantidadDisponibleTotal);

public sealed record ExistenciaUbicacionDto(
    string Sku,
    string UbicacionId,
    int CantidadFisica,
    int CantidadReservada,
    [property: JsonPropertyName("disponible")] int Disponible,
    string? TipoUbicacion = null,
    int Version = 0)
{
    [JsonPropertyName("cantidadDisponible")]
    public int CantidadDisponible => Disponible;
}

public sealed class GatewayClient
{
    private readonly HttpClient _http;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString,
        Converters = { new JsonStringEnumConverter(), new DecimalComoTextoConverter() }
    };

    public GatewayClient(HttpClient http, IHttpContextAccessor httpContextAccessor)
    {
        _http = http;
        _httpContextAccessor = httpContextAccessor;
    }

    private void SetAuthHeader(string? explicitToken = null)
    {
        var token = explicitToken
            ?? _httpContextAccessor.HttpContext?.Request.Cookies["sgo_jwt"]
            ?? _httpContextAccessor.HttpContext?.Items["sgo_jwt"] as string;

        if (!string.IsNullOrWhiteSpace(token))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            _http.DefaultRequestHeaders.Authorization = null;
        }

        var guestId = _httpContextAccessor.HttpContext?.Request.Cookies["sgo_guest_id"]
            ?? _httpContextAccessor.HttpContext?.Items["sgo_guest_id"] as string;

        if (_http.DefaultRequestHeaders.Contains("X-Session-Id"))
            _http.DefaultRequestHeaders.Remove("X-Session-Id");

        if (!string.IsNullOrWhiteSpace(guestId))
        {
            _http.DefaultRequestHeaders.Add("X-Session-Id", guestId);
        }
    }

    private static async Task<string> ParseProblemDetailsOrStringAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "Credenciales incorrectas o sesión no autorizada.",
                    System.Net.HttpStatusCode.Forbidden => "No tienes permisos para realizar esta acción.",
                    System.Net.HttpStatusCode.NotFound => "El recurso solicitado no fue encontrado.",
                    System.Net.HttpStatusCode.Conflict => "Conflicto con un recurso existente.",
                    System.Net.HttpStatusCode.BadRequest => "Solicitud inválida. Verifica los datos ingresados.",
                    _ => $"Error en la operación (código {(int)response.StatusCode})."
                };
            }

            var trimmed = raw.Trim();
            if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                // 1. Si viene detail (RFC 7807 ProblemDetails standard)
                if (root.TryGetProperty("detail", out var detailProp) && detailProp.ValueKind == JsonValueKind.String)
                {
                    var detail = detailProp.GetString();
                    if (!string.IsNullOrWhiteSpace(detail))
                        return detail;
                }

                // 2. Si vienen errores de validación (HttpValidationProblemDetails)
                if (root.TryGetProperty("errors", out var errorsProp) && errorsProp.ValueKind == JsonValueKind.Object)
                {
                    var errorList = new List<string>();
                    foreach (var prop in errorsProp.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var errItem in prop.Value.EnumerateArray())
                            {
                                var s = errItem.GetString();
                                if (!string.IsNullOrWhiteSpace(s))
                                    errorList.Add(s);
                            }
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            var s = prop.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(s))
                                errorList.Add(s);
                        }
                    }
                    if (errorList.Count > 0)
                        return string.Join(" ", errorList);
                }

                // 3. Si viene message
                if (root.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
                {
                    var msg = msgProp.GetString();
                    if (!string.IsNullOrWhiteSpace(msg))
                        return msg;
                }

                // 4. Si viene title
                if (root.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                {
                    var title = titleProp.GetString();
                    if (!string.IsNullOrWhiteSpace(title) && !title.Equals("One or more validation errors occurred.", StringComparison.OrdinalIgnoreCase))
                        return title;
                }
            }

            return trimmed;
        }
        catch
        {
            return response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? "Credenciales incorrectas o sesión no autorizada."
                : $"Error inesperado en la comunicación (código {(int)response.StatusCode}).";
        }
    }

    // ── Auth ───────────────────────────────────────────────────────────────────
    public async Task<(bool Exito, string? Error)> RegistrarAsync(RegistroModel model, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/api/auth/registro", model, ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, err);
    }

    public async Task<(bool Exito, string? Error)> VerificarAsync(VerificarModel model, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/api/auth/verificar", model, ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, err);
    }

    public async Task<(bool Exito, string? Error)> ReenviarCodigoAsync(string email, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/api/auth/reenviar-codigo", new { Email = email }, ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, err);
    }

    public async Task<(bool Exito, AuthResult? Result, string? Error)> LoginAsync(LoginModel model, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/api/auth/login", model, ct);
        if (res.IsSuccessStatusCode)
        {
            var data = await res.Content.ReadFromJsonAsync<AuthResult>(JsonOptions, ct);
            return (true, data, null);
        }
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, null, err);
    }

    public async Task<(bool Exito, string? Error)> AsignarRolAsync(string email, string rol, CancellationToken ct = default)
    {
        SetAuthHeader();
        var res = await _http.PostAsJsonAsync("/api/auth/asignar-rol", new { Email = email, Rol = rol }, ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, err);
    }

    public async Task<UsuarioDto?> ObtenerUsuarioPorEmailAsync(string email, CancellationToken ct = default)
    {
        SetAuthHeader();
        var res = await _http.GetAsync($"/api/auth/usuario?email={Uri.EscapeDataString(email)}", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<UsuarioDto>(JsonOptions, ct);
    }

    public async Task<List<UsuarioDto>> ListarUsuariosAsync(CancellationToken ct = default)
    {
        SetAuthHeader();
        var res = await _http.GetAsync("/api/auth/usuarios", ct);
        if (!res.IsSuccessStatusCode) return new List<UsuarioDto>();
        return await res.Content.ReadFromJsonAsync<List<UsuarioDto>>(JsonOptions, ct) ?? new List<UsuarioDto>();
    }

    // ── Carrito ────────────────────────────────────────────────────────────────
    public async Task<CarritoModel?> ObtenerCarritoAsync(CancellationToken ct = default)
    {
        SetAuthHeader();
        var res = await _http.GetAsync("/api/carrito", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<CarritoModel>(JsonOptions, ct);
    }

    public async Task<CarritoModel?> ObtenerCarritoPorIdAsync(string sesionOClienteId, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/carrito");
        req.Headers.Add("X-Session-Id", sesionOClienteId);
        var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<CarritoModel>(JsonOptions, ct);
    }

    public async Task<bool> AgregarItemCarritoAsync(AgregarItemModel model, CancellationToken ct = default)
    {
        SetAuthHeader();
        var res = await _http.PostAsJsonAsync("/api/carrito/items", model, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> ActualizarCantidadItemCarritoAsync(string sku, int cantidad, CancellationToken ct = default)
    {
        SetAuthHeader();
        var res = await _http.PutAsJsonAsync($"/api/carrito/items/{sku}", new { Cantidad = cantidad }, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> EliminarItemCarritoAsync(string sku, CancellationToken ct = default)
    {
        SetAuthHeader();
        var res = await _http.DeleteAsync($"/api/carrito/items/{sku}", ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> AsociarCarritoAsync(string guestSessionId, string? token = null, CancellationToken ct = default)
    {
        SetAuthHeader(token);
        var res = await _http.PostAsJsonAsync("/api/carrito/asociar", new { SesionId = guestSessionId }, ct);
        return res.IsSuccessStatusCode;
    }

    // ── Estado ─────────────────────────────────────────────────────────────────
    public async Task<OrdenEstadoModel?> ObtenerEstadoOrdenAsync(string ordenId, CancellationToken ct = default)
    {
        var res = await _http.GetAsync($"/api/estado/ordenes/{ordenId}", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<OrdenEstadoModel>(JsonOptions, ct);
    }

    public async Task<HistorialClienteModel?> ObtenerHistorialClienteAsync(string clienteId, int pagina = 1, int tamano = 20, CancellationToken ct = default)
    {
        var res = await _http.GetAsync($"/api/estado/clientes/{clienteId}/ordenes?pagina={pagina}&tamano={tamano}", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<HistorialClienteModel>(JsonOptions, ct);
    }

    // ── Facturacion ────────────────────────────────────────────────────────────
    public async Task<FacturaModel?> ObtenerFacturaAsync(string ordenId, CancellationToken ct = default)
    {
        SetAuthHeader();
        var res = await _http.GetAsync($"/api/facturacion/documentos/{ordenId}", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<FacturaModel>(JsonOptions, ct);
    }

    // ── Informe de ventas / ERP ────────────────────────────────────────────────
    public async Task<InformeVentasModel?> ObtenerInformeVentasAsync(DateTime? desde, DateTime? hasta, CancellationToken ct = default)
    {
        SetAuthHeader();
        var filtros = new List<string>();
        if (desde.HasValue) filtros.Add($"desde={desde:yyyy-MM-dd}");
        if (hasta.HasValue) filtros.Add($"hasta={hasta:yyyy-MM-dd}");
        var res = await _http.GetAsync($"/api/erp/informe?{string.Join('&', filtros)}", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<InformeVentasModel>(JsonOptions, ct);
    }

    public async Task<(bool Exito, string? Error)> EjecutarConciliacionAsync(CancellationToken ct = default)
    {
        SetAuthHeader();
        var res = await _http.PostAsync("/api/erp/conciliar", null, ct);
        return res.IsSuccessStatusCode ? (true, null) : (false, await ParseProblemDetailsOrStringAsync(res, ct));
    }

    public async Task<List<EventoAuditoriaModel>> ObtenerAuditoriaAgregadoAsync(string aggregateId, CancellationToken ct = default)
    {
        SetAuthHeader();
        var res = await _http.GetAsync($"/api/auditoria/agregados/{Uri.EscapeDataString(aggregateId)}?tamano=50", ct);
        if (!res.IsSuccessStatusCode) return new();
        return (await res.Content.ReadFromJsonAsync<PaginaAuditoriaModel>(JsonOptions, ct))?.Items ?? new();
    }

    // ── Incidencias ────────────────────────────────────────────────────────────
    public async Task<(bool Exito, IncidenciaModel? Incidencia, string? Error)> CrearIncidenciaAsync(CrearIncidenciaModel model, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/api/incidencias", model, ct);
        if (res.IsSuccessStatusCode)
        {
            var data = await res.Content.ReadFromJsonAsync<IncidenciaModel>(JsonOptions, ct);
            return (true, data, null);
        }
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, null, err);
    }

    // ── Órdenes y Checkout ─────────────────────────────────────────────────────
    public async Task<(bool Exito, OrdenCreadaModel? Orden, string? Error)> CrearOrdenAsync(CrearOrdenModel model, CancellationToken ct = default)
    {
        SetAuthHeader();
        var res = await _http.PostAsJsonAsync("/api/ordenes", model, JsonOptions, ct);
        if (res.IsSuccessStatusCode)
        {
            var orden = await res.Content.ReadFromJsonAsync<OrdenCreadaModel>(JsonOptions, ct);
            return (true, orden, null);
        }
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, null, err);
    }

    public async Task<(bool Exito, OrdenCreadaModel? Orden, string? Error)> ConfirmarOrdenAsync(string ordenId, string? motivo = null, CancellationToken ct = default)
    {
        SetAuthHeader();
        var res = await _http.PostAsJsonAsync($"/api/ordenes/{ordenId}/confirmar", new { Motivo = motivo }, JsonOptions, ct);
        if (res.IsSuccessStatusCode)
        {
            var respuesta = await res.Content.ReadFromJsonAsync<ConfirmarOrdenResponse>(JsonOptions, ct);
            return (true, respuesta?.Orden, null);
        }
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, null, err);
    }

    /// <summary>
    /// Cancelacion de la orden por su cliente via POST /api/ordenes/{id}/cancelar.
    /// Ordenes valida que <paramref name="clienteId" /> sea el dueno (403) y que el
    /// estado lo admita (409); el detalle del ProblemDetails vuelve como error.
    /// </summary>
    public async Task<(bool Exito, string? Error)> CancelarOrdenAsync(string ordenId, string clienteId, string? motivo = null, CancellationToken ct = default)
    {
        SetAuthHeader();
        var res = await _http.PostAsJsonAsync(
            $"/api/ordenes/{Uri.EscapeDataString(ordenId)}/cancelar",
            new { ClienteId = clienteId, Motivo = motivo },
            JsonOptions,
            ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, err);
    }

    // ── Despacho ────────────────────────────────────────────────────────────────
    /// <summary>
    /// Consulta el estado real de despacho de una orden via GET /api/despacho/{ordenId}.
    /// Retorna null si el despacho todavía no existe para la orden (saga aún en etapas tempranas).
    /// </summary>
    public async Task<DespachoOrdenModel?> ObtenerDespachoOrdenAsync(string ordenId, CancellationToken ct = default)
    {
        SetAuthHeader();
        var res = await _http.GetAsync($"/api/despacho/{ordenId}", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<DespachoOrdenModel>(JsonOptions, ct);
    }

    public async Task<List<DespachoPreparacionDto>?> ObtenerColaPreparacionAsync(string ubicacionId, string? estado = null, CancellationToken ct = default)
    {
        SetAuthHeader();
        var url = $"/api/despacho/ubicaciones/{ubicacionId}/preparacion" + (!string.IsNullOrWhiteSpace(estado) ? $"?estado={estado}" : "");
        var res = await _http.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<List<DespachoPreparacionDto>>(JsonOptions, ct);
    }

    public async Task<(bool Exito, string? Error)> ActualizarEstadoDespachoAsync(string ordenId, string estado, string? detalle = null, CancellationToken ct = default)
    {
        SetAuthHeader();
        var payload = new { Estado = estado, Detalle = detalle, OcurridoEn = DateTime.UtcNow };
        var res = await _http.PostAsJsonAsync($"/api/despacho/{ordenId}/estados", payload, JsonOptions, ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, err);
    }

    public async Task<(bool Exito, string? Error)> ReasignarMetodoEntregaDespachoAsync(string ordenId, string nuevoMetodo, CancellationToken ct = default)
    {
        SetAuthHeader();
        var res = await _http.PutAsJsonAsync($"/api/despacho/{ordenId}/metodo-entrega", new { MetodoEntrega = nuevoMetodo }, JsonOptions, ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, err);
    }

    // ── Inventario ─────────────────────────────────────────────────────────────
    public async Task<List<ExistenciaSkuDto>?> ObtenerExistenciasSkuAsync(string sku, CancellationToken ct = default)
    {
        SetAuthHeader();
        var res = await _http.GetAsync($"/api/inventario/existencias/{sku}", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<List<ExistenciaSkuDto>>(JsonOptions, ct);
    }

    public async Task<List<UbicacionResumenDto>?> ObtenerUbicacionesInventarioAsync(CancellationToken ct = default)
    {
        SetAuthHeader();
        var res = await _http.GetAsync("/api/inventario/ubicaciones", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<List<UbicacionResumenDto>>(JsonOptions, ct);
    }

    public async Task<List<ExistenciaUbicacionDto>?> ObtenerExistenciasPorUbicacionAsync(string ubicacionId, CancellationToken ct = default)
    {
        SetAuthHeader();
        var res = await _http.GetAsync($"/api/inventario/ubicaciones/{ubicacionId}/existencias", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<List<ExistenciaUbicacionDto>>(JsonOptions, ct);
    }

    // ── Incidencias (Listar / Resolver) ────────────────────────────────────────
    public async Task<List<IncidenciaModel>?> ListarIncidenciasAsync(string? ordenId = null, string? estado = null, CancellationToken ct = default)
    {
        SetAuthHeader();
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(ordenId)) query.Add($"ordenId={Uri.EscapeDataString(ordenId)}");
        if (!string.IsNullOrWhiteSpace(estado)) query.Add($"estado={Uri.EscapeDataString(estado)}");
        var url = "/api/incidencias" + (query.Count > 0 ? "?" + string.Join("&", query) : "");

        var res = await _http.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<List<IncidenciaModel>>(JsonOptions, ct);
    }

    public async Task<(bool Exito, string? Error)> ActualizarIncidenciaAsync(Guid id, string? descripcion, string? estado, string? resolucion, CancellationToken ct = default)
    {
        SetAuthHeader();
        var payload = new { Descripcion = descripcion, Estado = estado, Resolucion = resolucion };
        var res = await _http.PutAsJsonAsync($"/api/incidencias/{id}", payload, JsonOptions, ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, err);
    }

    public async Task<(bool Exito, string? MensajeId, string? Error)> EnviarSmsAsync(string telefono, string mensaje, CancellationToken ct = default)
    {
        try
        {
            var payload = new { Destinatario = telefono, Asunto = "MegaStore Comprobante", Cuerpo = mensaje };
            var res = await _http.PostAsJsonAsync("/api/mensajes/sms", payload, JsonOptions, ct);
            if (res.IsSuccessStatusCode)
            {
                var doc = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: ct);
                var id = doc.TryGetProperty("mensajeId", out var idProp) ? idProp.GetString() : null;
                return (true, id, null);
            }
            var err = await ParseProblemDetailsOrStringAsync(res, ct);
            return (false, null, err);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    public async Task<(bool Exito, EnlaceReintentoInfoModel? Info, string? Error)> ObtenerReintentoPagoAsync(string token, CancellationToken ct = default)
    {
        try
        {
            var res = await _http.GetAsync($"/api/pagos/reintentos/{Uri.EscapeDataString(token)}", ct);
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (false, null, "El enlace de pago no existe o no fue encontrado.");
            }

            if (!res.IsSuccessStatusCode)
            {
                var err = await ParseProblemDetailsOrStringAsync(res, ct);
                return (false, null, err);
            }

            var info = await res.Content.ReadFromJsonAsync<EnlaceReintentoInfoModel>(JsonOptions, ct);
            return (true, info, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    public async Task<(bool Exito, PagoReintentoResultadoModel? Pago, string? Error)> EjecutarReintentoPagoAsync(string token, CancellationToken ct = default)
    {
        try
        {
            var res = await _http.PostAsync($"/api/pagos/reintentos/{Uri.EscapeDataString(token)}", null, ct);
            if (res.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                return (false, null, "El enlace de reintento ya se utilizó o ha expirado.");
            }

            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (false, null, "El enlace de reintento no fue encontrado.");
            }

            if (!res.IsSuccessStatusCode)
            {
                var err = await ParseProblemDetailsOrStringAsync(res, ct);
                return (false, null, err);
            }

            var pago = await res.Content.ReadFromJsonAsync<PagoReintentoResultadoModel>(JsonOptions, ct);
            return (true, pago, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }
}

