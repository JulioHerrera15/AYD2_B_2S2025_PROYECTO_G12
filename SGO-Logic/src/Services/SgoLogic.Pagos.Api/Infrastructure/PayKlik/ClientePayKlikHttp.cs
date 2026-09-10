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
/// </summary>
public sealed class ClientePayKlikHttp : IClientePayKlik
{
    private readonly HttpClient _http;

    public ClientePayKlikHttp(HttpClient http)
    {
        _http = http;
    }

    public async Task<ResultadoPayKlik> CargarAsync(PeticionCargoPayKlik peticion, CancellationToken cancelacion)
    {
        using var respuesta = await _http.PostAsJsonAsync(
            "/v1/cargos", peticion, SgoJsonSerializerOptions.Default, cancelacion);

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
