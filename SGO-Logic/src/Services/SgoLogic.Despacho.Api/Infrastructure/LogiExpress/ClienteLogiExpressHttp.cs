using System.Net.Http.Json;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Despacho.Api.Application.Despacho;

namespace SgoLogic.Despacho.Api.Infrastructure.LogiExpress;

/// <summary>
/// Cliente de LogiExpress. Una sola llamada, timeout corto, sin reintentos y sin
/// circuit breaker: el criterio de aceptacion de HU-011 dice que ante un timeout el
/// despacho fisico continua igual, asi que fallar rapido es la conducta correcta.
/// Reintentar solo retrasaria algo que de todos modos va a seguir sin guia.
/// </summary>
public sealed class ClienteLogiExpressHttp : IClienteLogiExpress
{
    private readonly HttpClient _http;

    public ClienteLogiExpressHttp(HttpClient http)
    {
        _http = http;
    }

    public async Task<GuiaEmitidaDto?> SolicitarGuiaAsync(SolicitudGuia solicitud, CancellationToken cancelacion)
    {
        var respuesta = await _http.PostAsJsonAsync(
            "/api/logiexpress/guias",
            new PeticionGuiaDto(
                solicitud.OrdenId,
                solicitud.UbicacionOrigenId,
                solicitud.DireccionEntrega,
                solicitud.Lineas.Select(l => new LineaDto(l.Sku, l.Cantidad, l.Descripcion)).ToList()),
            SgoJsonSerializerOptions.Default,
            cancelacion);

        if (!respuesta.IsSuccessStatusCode)
        {
            // El proveedor contesto pero no emitio. Es resultado de negocio: null
            // deja el despacho en GUIA_PENDIENTE sin lanzar.
            return null;
        }

        var guia = await respuesta.Content.ReadFromJsonAsync<RespuestaGuiaDto>(
            SgoJsonSerializerOptions.Default, cancelacion);

        return guia is null || string.IsNullOrWhiteSpace(guia.GuiaId)
            ? null
            : new GuiaEmitidaDto(guia.GuiaId, guia.TrackingUrl ?? string.Empty, guia.Transportista ?? "LogiExpress");
    }

    public async Task<bool> AnularGuiaAsync(string guiaId, CancellationToken cancelacion)
    {
        var respuesta = await _http.DeleteAsync($"/api/logiexpress/guias/{guiaId}", cancelacion);
        return respuesta.IsSuccessStatusCode;
    }

    private sealed record PeticionGuiaDto(
        string OrdenId, string UbicacionOrigenId, string DireccionEntrega, IReadOnlyList<LineaDto> Lineas);

    private sealed record LineaDto(string Sku, int Cantidad, string? Descripcion);

    private sealed record RespuestaGuiaDto(string GuiaId, string? TrackingUrl, string? Transportista);
}
