using System.Net.Http.Json;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Pagos.Api.Application.Pagos;

namespace SgoLogic.Pagos.Api.Infrastructure.PayKlik;

/// <summary>
/// Cliente REST real del proveedor PayKlik. El <see cref="HttpClient" /> viene
/// configurado con BaseAddress y Timeout desde la seccion "PayKlik" (ver
/// Program.cs). Un timeout del cliente lanza <see cref="OperationCanceledException" />;
/// una respuesta 5xx o un cuerpo invalido, <see cref="PayKlikNoDisponibleException" />.
/// El servicio de cobro traduce cualquiera de esas a pago PENDIENTE.
///
/// Envia el header <c>Idempotency-Key</c> con el identificador del PAGO (no del
/// intento) para que los reintentos no generen doble cargo. No lleva circuit
/// breaker: de eso se encarga <see cref="ClientePayKlikConDisyuntor" />.
/// </summary>
public sealed class ClientePayKlikHttp : IClientePayKlik
{
    private readonly HttpClient _http;

    public ClientePayKlikHttp(HttpClient http)
    {
        _http = http;
    }

    public async Task<ResultadoPayKlik> CargarAsync(
        PeticionCargoPayKlik peticion,
        string claveIdempotencia,
        CancellationToken cancelacion)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, "/v1/cargos")
        {
            Content = JsonContent.Create(peticion, mediaType: null, SgoJsonSerializerOptions.Default)
        };

        if (!string.IsNullOrWhiteSpace(claveIdempotencia))
        {
            solicitud.Headers.Add("Idempotency-Key", claveIdempotencia);
        }

        using var respuesta = await _http.SendAsync(solicitud, cancelacion);

        if ((int)respuesta.StatusCode >= 500)
        {
            throw new PayKlikNoDisponibleException($"PayKlik respondio {(int)respuesta.StatusCode}.");
        }

        respuesta.EnsureSuccessStatusCode();

        var resultado = await respuesta.Content.ReadFromJsonAsync<ResultadoPayKlik>(
            SgoJsonSerializerOptions.Default, cancelacion);

        return resultado ?? throw new PayKlikNoDisponibleException("PayKlik devolvio un cuerpo vacio.");
    }
}
