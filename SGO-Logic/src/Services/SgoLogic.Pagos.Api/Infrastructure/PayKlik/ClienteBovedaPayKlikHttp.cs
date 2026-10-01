using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SgoLogic.Pagos.Api.Application.Tarjetas;

namespace SgoLogic.Pagos.Api.Infrastructure.PayKlik;

/// <summary>
/// Cliente HTTP de POST /v1/tarjetas/tokens de PayKlik. Servidor a servidor: el CVV
/// viaja solo en el cuerpo de esta peticion y no se registra (sin logging de
/// cuerpos, y el mensaje de error nunca lo incluye).
/// </summary>
public sealed class ClienteBovedaPayKlikHttp : IClienteBovedaPayKlik
{
    private static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public ClienteBovedaPayKlikHttp(HttpClient http)
    {
        _http = http;
    }

    public async Task<ResultadoTokenBoveda> GenerarTokenAsync(string referenciaTarjeta, string cvv, CancellationToken cancelacion)
    {
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _http.PostAsJsonAsync(
                "/v1/tarjetas/tokens",
                new { referenciaTarjeta, cvv },
                OpcionesJson,
                cancelacion);
        }
        catch (HttpRequestException)
        {
            return new ResultadoTokenBoveda(EstadoTokenBoveda.NoDisponible);
        }
        catch (TaskCanceledException) when (!cancelacion.IsCancellationRequested)
        {
            return new ResultadoTokenBoveda(EstadoTokenBoveda.NoDisponible);
        }

        using (respuesta)
        {
            if (respuesta.IsSuccessStatusCode)
            {
                try
                {
                    var token = await respuesta.Content.ReadFromJsonAsync<TokenTarjetaDto>(OpcionesJson, cancelacion);
                    return token is null
                        ? new ResultadoTokenBoveda(EstadoTokenBoveda.NoDisponible)
                        : new ResultadoTokenBoveda(EstadoTokenBoveda.Emitido, token);
                }
                catch (JsonException)
                {
                    return new ResultadoTokenBoveda(EstadoTokenBoveda.NoDisponible);
                }
            }

            return respuesta.StatusCode switch
            {
                HttpStatusCode.NotFound => new ResultadoTokenBoveda(EstadoTokenBoveda.ReferenciaNoExiste),
                HttpStatusCode.UnprocessableEntity => new ResultadoTokenBoveda(
                    EstadoTokenBoveda.Rechazado,
                    Mensaje: await LeerDetalleAsync(respuesta, cancelacion)),
                _ => new ResultadoTokenBoveda(EstadoTokenBoveda.NoDisponible)
            };
        }
    }

    private static async Task<string?> LeerDetalleAsync(HttpResponseMessage respuesta, CancellationToken cancelacion)
    {
        try
        {
            using var documento = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(cancelacion));
            return documento.RootElement.TryGetProperty("detail", out var detalle) && detalle.ValueKind == JsonValueKind.String
                ? detalle.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
