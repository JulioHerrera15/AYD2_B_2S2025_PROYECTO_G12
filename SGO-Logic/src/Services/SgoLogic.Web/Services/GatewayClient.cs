using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using SgoLogic.BuildingBlocks.Serialization;

namespace SgoLogic.Web.Services;

public sealed record RegistroModel(string Email, string Contrasena, string NombreCompleto);
public sealed record VerificarModel(string Email, string Codigo);
public sealed record LoginModel(string Email, string Contrasena);
public sealed record AuthResult(string Token, Guid UsuarioId, string Email, string NombreCompleto, DateTime ExpiraEn);

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
    string? MotivoUltimoCambio = null);

public sealed record CrearIncidenciaModel(string OrdenId, string Descripcion);

// ── Modelos de Órdenes y Checkout ───────────────────────────────────────────
public sealed record LineaCrearOrdenModel(string Sku, string Descripcion, int Cantidad, decimal PrecioUnitario);

public sealed record CrearOrdenModel(
    string ClienteId,
    string Canal,
    string MetodoEntrega,
    string Moneda,
    string MedioPago,
    decimal Impuestos,
    List<LineaCrearOrdenModel> Lineas,
    string? UbicacionSolicitadaId = null,
    string? ReceptorCui = null);

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
    DateTime ActualizadaEn);

public sealed record HistorialClienteModel(
    string ClienteId,
    int Pagina,
    int Tamano,
    int TotalRegistros,
    List<OrdenHistorialResumenModel> Ordenes);

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

    // ── Auth ───────────────────────────────────────────────────────────────────
    public async Task<(bool Exito, string? Error)> RegistrarAsync(RegistroModel model, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/api/auth/registro", model, ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await res.Content.ReadAsStringAsync(ct);
        return (false, err);
    }

    public async Task<(bool Exito, string? Error)> VerificarAsync(VerificarModel model, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/api/auth/verificar", model, ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await res.Content.ReadAsStringAsync(ct);
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
        var err = await res.Content.ReadAsStringAsync(ct);
        return (false, null, err);
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

    // ── Incidencias ────────────────────────────────────────────────────────────
    public async Task<(bool Exito, IncidenciaModel? Incidencia, string? Error)> CrearIncidenciaAsync(CrearIncidenciaModel model, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/api/incidencias", model, ct);
        if (res.IsSuccessStatusCode)
        {
            var data = await res.Content.ReadFromJsonAsync<IncidenciaModel>(JsonOptions, ct);
            return (true, data, null);
        }
        var err = await res.Content.ReadAsStringAsync(ct);
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
        var err = await res.Content.ReadAsStringAsync(ct);
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
        var err = await res.Content.ReadAsStringAsync(ct);
        return (false, null, err);
    }
}
