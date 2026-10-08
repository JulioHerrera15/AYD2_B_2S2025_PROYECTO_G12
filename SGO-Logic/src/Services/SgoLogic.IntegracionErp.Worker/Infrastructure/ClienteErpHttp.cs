using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SgoLogic.IntegracionErp.Worker.Infrastructure;

public sealed record RespuestaErp(bool Exitoso, bool Duplicado, string Estado, int? CodigoHttp, string? Detalle, string? Raw);

public interface IClienteErp
{
    Task<RespuestaErp> EnviarLoteAsync(string contenido, string contentType, CancellationToken cancelacion = default);
    Task<ResumenErp?> ObtenerResumenAsync(CancellationToken cancelacion = default);
    Task<IReadOnlyList<ClienteHistorialErp>> ObtenerHistorialClientesAsync(DateTime? modificadosDesde = null, CancellationToken cancelacion = default);
}

public sealed record ResumenErp(long UltimaSecuenciaAceptada, IReadOnlyList<ConfirmacionErp> Lotes);
public sealed record ConfirmacionErp(string LoteId, long Secuencia, string Estado, int CantidadRegistros, decimal MontoTotal);
public sealed record ClienteHistorialErp(
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
    string? LoteOrigen = null);

public sealed class ClienteErpHttp : IClienteErp
{
    private readonly HttpClient _http;
    private readonly ErpOptions _opciones;
    public ClienteErpHttp(HttpClient http, IOptions<ErpOptions> opciones) { _http = http; _opciones = opciones.Value; }

    public async Task<RespuestaErp> EnviarLoteAsync(string contenido, string contentType, CancellationToken cancelacion = default)
    {
        for (var intento = 0; ; intento++)
        {
            try
            {
                using var body = new StringContent(contenido, Encoding.UTF8, contentType);
                using var respuesta = await _http.PostAsync("/erp/lotes", body, cancelacion);
                var raw = await respuesta.Content.ReadAsStringAsync(cancelacion);
                var estado = LeerEstado(raw, respuesta.StatusCode.ToString());
                if (respuesta.IsSuccessStatusCode || (respuesta.StatusCode == HttpStatusCode.Conflict && estado == "DUPLICADO"))
                    return new RespuestaErp(true, estado == "DUPLICADO", estado, (int)respuesta.StatusCode, null, raw);
                if (!EsReintentable(respuesta.StatusCode) || intento >= _opciones.MaxReintentos)
                    return new RespuestaErp(false, false, estado, (int)respuesta.StatusCode, raw, raw);
                await Task.Delay(Backoff(intento), cancelacion);
            }
            catch (OperationCanceledException) when (!cancelacion.IsCancellationRequested)
            {
                if (intento >= _opciones.MaxReintentos) return new RespuestaErp(false, false, "TIMEOUT", 504, "Timeout ERP", null);
                await Task.Delay(Backoff(intento), cancelacion);
            }
            catch (HttpRequestException ex)
            {
                if (intento >= _opciones.MaxReintentos) return new RespuestaErp(false, false, "ERROR_RED", 500, ex.Message, ex.Message);
                await Task.Delay(Backoff(intento), cancelacion);
            }
        }
    }

    public async Task<ResumenErp?> ObtenerResumenAsync(CancellationToken cancelacion = default)
    {
        using var respuesta = await _http.GetAsync("/erp/lotes", cancelacion);
        if (!respuesta.IsSuccessStatusCode) return null;
        using var documento = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(cancelacion));
        var root = documento.RootElement;
        var lotes = root.GetProperty("lotes").EnumerateArray().Select(x => new ConfirmacionErp(
            x.GetProperty("loteId").GetString() ?? "", x.GetProperty("secuencia").GetInt64(), x.GetProperty("estado").GetString() ?? "",
            x.TryGetProperty("cantidadRegistrosProcesados", out var c) ? c.GetInt32() : 0,
            x.TryGetProperty("montoTotalProcesado", out var m) ? m.GetDecimal() : 0m)).ToList();
        return new ResumenErp(root.GetProperty("ultimaSecuenciaAceptada").GetInt64(), lotes);
    }

    public async Task<IReadOnlyList<ClienteHistorialErp>> ObtenerHistorialClientesAsync(DateTime? modificadosDesde = null, CancellationToken cancelacion = default)
    {
        var url = "/erp/clientes/historial";
        if (modificadosDesde.HasValue)
            url += $"?modificadosDesde={Uri.EscapeDataString(modificadosDesde.Value.ToString("O"))}";

        for (var intento = 0; ; intento++)
        {
            try
            {
                using var respuesta = await _http.GetAsync(url, cancelacion);
                var raw = await respuesta.Content.ReadAsStringAsync(cancelacion);
                if (!respuesta.IsSuccessStatusCode)
                {
                    if (intento >= _opciones.MaxReintentos || !EsReintentable((HttpStatusCode)respuesta.StatusCode))
                        return [];
                    await Task.Delay(Backoff(intento), cancelacion);
                    continue;
                }

                using var documento = JsonDocument.Parse(raw);
                var root = documento.RootElement;
                if (!root.TryGetProperty("clientes", out var clientesElement) || clientesElement.ValueKind != JsonValueKind.Array)
                    return [];

                var resultados = new List<ClienteHistorialErp>();
                foreach (var item in clientesElement.EnumerateArray())
                {
                    var codigoCliente = item.TryGetProperty("codigoCliente", out var codigo) ? codigo.GetString() ?? string.Empty : string.Empty;
                    var email = item.TryGetProperty("email", out var correo) ? correo.GetString() ?? string.Empty : string.Empty;
                    var clienteDesde = item.TryGetProperty("clienteDesde", out var desde) ? desde.GetDateTime() : DateTime.UtcNow;
                    var compras = item.TryGetProperty("comprasUltimos12Meses", out var comprasItem) ? comprasItem.GetInt32() : 0;
                    var monto = item.TryGetProperty("montoCompradoUltimos12Meses", out var montoItem) ? montoItem.GetDecimal() : 0m;
                    var moneda = item.TryGetProperty("moneda", out var monedaItem) ? monedaItem.GetString() ?? "GTQ" : "GTQ";
                    var puntuales = item.TryGetProperty("pagosPuntuales", out var puntualesItem) ? puntualesItem.GetInt32() : 0;
                    var tardios = item.TryGetProperty("pagosTardios", out var tardiosItem) ? tardiosItem.GetInt32() : 0;
                    var maxDias = item.TryGetProperty("maxDiasAtraso", out var atrasosItem) ? atrasosItem.GetInt32() : 0;
                    var devoluciones = item.TryGetProperty("devoluciones", out var devolucionesItem) ? devolucionesItem.GetInt32() : 0;
                    var incidentesGraves = item.TryGetProperty("incidentesGraves", out var gravesItem) ? gravesItem.GetInt32() : 0;
                    var otros = item.TryGetProperty("otrosIncidentes", out var otrosItem) ? otrosItem.GetInt32() : 0;
                    var fechaCorte = item.TryGetProperty("fechaCorte", out var corteItem) ? corteItem.GetDateTime() : DateTime.UtcNow;
                    var actualizadoEn = item.TryGetProperty("actualizadoEn", out var actualizadoItem) ? actualizadoItem.GetDateTime() : fechaCorte;
                    var loteOrigen = item.TryGetProperty("loteOrigen", out var loteItem) ? loteItem.GetString() : null;
                    resultados.Add(new ClienteHistorialErp(codigoCliente, email, clienteDesde, compras, monto, moneda, puntuales, tardios, maxDias, devoluciones, incidentesGraves, otros, fechaCorte, actualizadoEn, loteOrigen));
                }

                return resultados;
            }
            catch (OperationCanceledException) when (!cancelacion.IsCancellationRequested)
            {
                if (intento >= _opciones.MaxReintentos) return [];
                await Task.Delay(Backoff(intento), cancelacion);
            }
            catch (HttpRequestException)
            {
                if (intento >= _opciones.MaxReintentos) return [];
                await Task.Delay(Backoff(intento), cancelacion);
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }

    private static bool EsReintentable(HttpStatusCode status) => status is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
    private static TimeSpan Backoff(int intento) => TimeSpan.FromMilliseconds(Math.Min(250 * Math.Pow(2, intento), 4000));
    private static string LeerEstado(string raw, string fallback)
    {
        try { using var doc = JsonDocument.Parse(raw); return doc.RootElement.TryGetProperty("estado", out var estado) ? estado.GetString() ?? fallback : fallback; }
        catch (JsonException) { return fallback; }
    }
}
