using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// Un 422 o 404 con un codigo de rechazo del medio de pago (tarjeta invalida, CVV
/// invalido, vencida o referencia de boveda inexistente) se lanza como
/// <see cref="TokenMedioPagoInvalidoException" />: es un error del cliente, no una
/// falla del proveedor.
///
/// El cuerpo del cargo lleva datos de tarjeta: este cliente no registra peticiones ni
/// cuerpos, y ningun mensaje de excepcion los incluye.
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

        await LanzarSiTokenNoValidoAsync(respuesta, cancelacion);

        respuesta.EnsureSuccessStatusCode();

        var resultado = await respuesta.Content.ReadFromJsonAsync<ResultadoPayKlik>(
            SgoJsonSerializerOptions.Default, cancelacion);

        return resultado ?? throw new PayKlikNoDisponibleException("PayKlik devolvio un cuerpo vacio.");
    }

    /// <summary>
    /// Un 422 o 404 con un codigo de <see cref="CodigosToken.DelProveedor" /> en el
    /// cuerpo es un medio de pago que PayKlik no acepta. Se distingue del rechazo por
    /// fondos (200 con estado RECHAZADO) y de una caida (5xx), y se lanza como
    /// <see cref="TokenMedioPagoInvalidoException" />, que el disyuntor no cuenta como
    /// falla del proveedor. Cualquier otro 4xx sigue el camino de siempre.
    /// </summary>
    private static async Task LanzarSiTokenNoValidoAsync(HttpResponseMessage respuesta, CancellationToken cancelacion)
    {
        if (respuesta.StatusCode is not (HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound))
        {
            return;
        }

        string? codigo = null;
        string? detalle = null;

        try
        {
            using var documento = await JsonDocument.ParseAsync(
                await respuesta.Content.ReadAsStreamAsync(cancelacion), cancellationToken: cancelacion);

            if (documento.RootElement.ValueKind == JsonValueKind.Object)
            {
                codigo = documento.RootElement.TryGetProperty("codigo", out var elementoCodigo)
                    ? elementoCodigo.GetString()
                    : null;
                detalle = documento.RootElement.TryGetProperty("detail", out var elementoDetalle)
                    ? elementoDetalle.GetString()
                    : null;
            }
        }
        catch (JsonException)
        {
            return;
        }

        if (codigo is not null && CodigosToken.DelProveedor.Contains(codigo))
        {
            throw new TokenMedioPagoInvalidoException(
                codigo, detalle ?? "PayKlik rechazo el medio de pago.");
        }
    }

    public async Task<ResultadoPayKlik> ReembolsoAsync(
        PeticionReembolsoPayKlik peticion,
        string claveIdempotencia,
        CancellationToken cancelacion)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, "/v1/reembolsos")
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
            throw new PayKlikNoDisponibleException($"PayKlik respondio {(int)respuesta.StatusCode} en reembolso.");
        }

        respuesta.EnsureSuccessStatusCode();

        var resultado = await respuesta.Content.ReadFromJsonAsync<ResultadoPayKlik>(
            SgoJsonSerializerOptions.Default, cancelacion);

        return resultado ?? throw new PayKlikNoDisponibleException("PayKlik devolvio un cuerpo vacio en reembolso.");
    }
}
