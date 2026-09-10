using System.Net.Http.Json;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Facturacion.Api.Application.Facturacion;

namespace SgoLogic.Facturacion.Api.Infrastructure.Sat;

/// <summary>
/// Cliente REST real del servicio de la SAT (mock <c>SgoLogic.Mocks.SAT</c>). El
/// <see cref="HttpClient" /> viene configurado con BaseAddress y Timeout desde la
/// seccion "Sat" (ver Program.cs). Un timeout del cliente lanza
/// <see cref="OperationCanceledException" />; una respuesta 5xx o un cuerpo
/// invalido, <see cref="SatNoDisponibleException" />. Un rechazo explicito de la
/// SAT (200 con estado RECHAZADO) vuelve como resultado, no como excepcion.
///
/// Envia el header <c>Idempotency-Key</c> con el identificador del DOCUMENTO fiscal
/// para que un reintento no genere una certificacion duplicada.
/// </summary>
public sealed class ClienteSatHttp : IClienteSat
{
    private readonly HttpClient _http;

    public ClienteSatHttp(HttpClient http)
    {
        _http = http;
    }

    public async Task<ResultadoCertificacionSat> CertificarAsync(
        PeticionCertificacionSat peticion,
        string claveIdempotencia,
        CancellationToken cancelacion)
    {
        var cuerpo = new
        {
            serie = peticion.Serie,
            numeroDocumento = peticion.NumeroDocumento,
            cuiReceptor = peticion.CuiReceptor,
            xml = peticion.Xml,
            total = peticion.Total,
            moneda = peticion.Moneda.ToString()
        };

        using var solicitud = new HttpRequestMessage(HttpMethod.Post, "/v1/certificaciones")
        {
            Content = JsonContent.Create(cuerpo, mediaType: null, SgoJsonSerializerOptions.Default)
        };

        if (!string.IsNullOrWhiteSpace(claveIdempotencia))
        {
            solicitud.Headers.Add("Idempotency-Key", claveIdempotencia);
        }

        using var respuesta = await _http.SendAsync(solicitud, cancelacion);

        if ((int)respuesta.StatusCode >= 500)
        {
            throw new SatNoDisponibleException($"La SAT respondio {(int)respuesta.StatusCode}.");
        }

        respuesta.EnsureSuccessStatusCode();

        var dto = await respuesta.Content.ReadFromJsonAsync<RespuestaSatDto>(
            SgoJsonSerializerOptions.Default, cancelacion)
            ?? throw new SatNoDisponibleException("La SAT devolvio un cuerpo vacio.");

        var estado = string.Equals(dto.Estado, "CERTIFICADO", StringComparison.OrdinalIgnoreCase)
            ? EstadoCertificacionSat.CERTIFICADO
            : EstadoCertificacionSat.RECHAZADO;

        return new ResultadoCertificacionSat(
            estado,
            dto.Folio,
            dto.NumeroAutorizacion,
            dto.CodigoRespuesta ?? string.Empty,
            dto.Mensaje ?? string.Empty,
            dto.CertificadoEn ?? DateTime.UtcNow);
    }

    private sealed record RespuestaSatDto(
        string? Folio,
        string? NumeroAutorizacion,
        string? Estado,
        string? CodigoRespuesta,
        string? Mensaje,
        DateTime? CertificadoEn);
}
