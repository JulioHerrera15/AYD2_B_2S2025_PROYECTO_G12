using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SgoLogic.IntegracionPos.Worker.Infrastructure.Pos;

public sealed class ClientePosHttp : Application.Pos.IClientePos
{
    private readonly HttpClient _http;
    private readonly PosOptions _opciones;

    public ClientePosHttp(HttpClient http, IOptions<PosOptions> opciones)
    {
        _http = http;
        _opciones = opciones.Value;
    }

    public async Task<Application.Pos.RespuestaPos> EnviarAsync(
        string tiendaId,
        string posInstanciaId,
        string eventoId,
        string ordenId,
        string tipo,
        decimal total,
        CancellationToken cancelacion = default)
    {
        var url = $"/pos/{posInstanciaId}/eventos";
        var payload = new
        {
            eventoId,
            ordenId,
            tipo,
            total,
            tiendaId,
            fecha = DateTime.UtcNow
        };

        var intento = 0;
        while (true)
        {
            try
            {
                using var respuesta = await _http.PostAsJsonAsync(url, payload, cancelacion);
                var texto = await respuesta.Content.ReadAsStringAsync(cancelacion);
                var estado = respuesta.StatusCode;

                if (respuesta.IsSuccessStatusCode)
                {
                    var resultado = JsonDocument.Parse(texto).RootElement;
                    var estadoResp = resultado.TryGetProperty("estado", out var prop) ? prop.GetString() ?? "ACEPTADO" : "ACEPTADO";
                    var duplicado = estadoResp.Equals("DUPLICADO", StringComparison.OrdinalIgnoreCase);
                    return new Application.Pos.RespuestaPos(
                        Aceptado: !duplicado,
                        Duplicado: duplicado,
                        Estado: estadoResp,
                        CodigoHttp: (int)estado,
                        Mensaje: resultado.TryGetProperty("mensaje", out var msg) ? msg.GetString() : null,
                        ProcesadoEn: resultado.TryGetProperty("procesadoEn", out var proc) && DateTime.TryParse(proc.GetString(), out var dt) ? dt : null,
                        RespuestaRaw: texto);
                }

                if (estado == HttpStatusCode.ServiceUnavailable || estado == HttpStatusCode.InternalServerError || estado == HttpStatusCode.GatewayTimeout)
                {
                    if (intento < _opciones.MaxReintentos)
                    {
                        intento++;
                        await Task.Delay(CalcularBackoff(intento), cancelacion);
                        continue;
                    }

                    return new Application.Pos.RespuestaPos(false, false, estado.ToString(), (int)estado, texto, null, texto);
                }

                return new Application.Pos.RespuestaPos(false, false, estado.ToString(), (int)estado, texto, null, texto);
            }
            catch (OperationCanceledException) when (!cancelacion.IsCancellationRequested)
            {
                if (intento < _opciones.MaxReintentos)
                {
                    intento++;
                    await Task.Delay(CalcularBackoff(intento), cancelacion);
                    continue;
                }

                return new Application.Pos.RespuestaPos(false, false, "TIMEOUT", 504, "Timeout al contactar POS", null, null);
            }
            catch (Exception ex)
            {
                if (intento < _opciones.MaxReintentos)
                {
                    intento++;
                    await Task.Delay(CalcularBackoff(intento), cancelacion);
                    continue;
                }

                return new Application.Pos.RespuestaPos(false, false, "ERROR_RED", 500, ex.Message, null, ex.Message);
            }
        }
    }

    private TimeSpan CalcularBackoff(int intento)
        => TimeSpan.FromMilliseconds(Math.Min(250 * Math.Pow(2, intento - 1), 4000));
}

public sealed class PosOptions
{
    public const string Seccion = "Pos";
    public string BaseUrl { get; set; } = "http://localhost:4001";
    public int TiempoEsperaMs { get; set; } = 5000;
    public int MaxReintentos { get; set; } = 3;
}
