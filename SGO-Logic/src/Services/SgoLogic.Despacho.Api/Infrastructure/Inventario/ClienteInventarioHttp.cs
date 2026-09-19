using System.Net;
using System.Net.Http.Json;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Despacho.Api.Application.Despacho;

namespace SgoLogic.Despacho.Api.Infrastructure.Inventario;

/// <summary>
/// Consulta a Inventario desde que ubicacion se reservo, via
/// <c>GET /api/inventario/reservas/{ordenId}</c> (endpoint de HU-001).
///
/// Sin circuit breaker a proposito: si Inventario esta caido, Despacho no tiene
/// plan B, asi que la excepcion sube y la cola reintenta. Un disyuntor solo
/// cambiaria el mensaje de error, no el desenlace.
/// </summary>
public sealed class ClienteInventarioHttp : IClienteInventario
{
    private readonly HttpClient _http;

    public ClienteInventarioHttp(HttpClient http)
    {
        _http = http;
    }

    public async Task<UbicacionReservaDto?> ObtenerUbicacionDeReservaAsync(string ordenId, CancellationToken cancelacion)
    {
        var respuesta = await _http.GetAsync($"/api/inventario/reservas/{ordenId}", cancelacion);

        // 404 no es un fallo: significa que Inventario no tiene reserva de esa orden,
        // y quien llama decide si usa la ubicacion sugerida o se rinde.
        if (respuesta.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        respuesta.EnsureSuccessStatusCode();

        var reserva = await respuesta.Content.ReadFromJsonAsync<ReservaInventarioDto>(
            SgoJsonSerializerOptions.Default, cancelacion);

        return reserva is null || string.IsNullOrWhiteSpace(reserva.UbicacionId)
            ? null
            : new UbicacionReservaDto(reserva.UbicacionId, reserva.TipoUbicacion);
    }

    private sealed record ReservaInventarioDto(string UbicacionId, TipoUbicacion TipoUbicacion);
}
