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
}

public sealed record ResumenErp(long UltimaSecuenciaAceptada, IReadOnlyList<ConfirmacionErp> Lotes);
public sealed record ConfirmacionErp(string LoteId, long Secuencia, string Estado, int CantidadRegistros, decimal MontoTotal);

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

    private static bool EsReintentable(HttpStatusCode status) => status is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
    private static TimeSpan Backoff(int intento) => TimeSpan.FromMilliseconds(Math.Min(250 * Math.Pow(2, intento), 4000));
    private static string LeerEstado(string raw, string fallback)
    {
        try { using var doc = JsonDocument.Parse(raw); return doc.RootElement.TryGetProperty("estado", out var estado) ? estado.GetString() ?? fallback : fallback; }
        catch (JsonException) { return fallback; }
    }
}
