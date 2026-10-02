using Microsoft.Extensions.Logging;
using SgoLogic.Ordenes.Api.Application.Ordenes;

namespace SgoLogic.Ordenes.Api.Infrastructure.Carrito;

/// <summary>
/// Llama a <c>DELETE /interno/carrito/{clienteId}</c> en Auth para vaciar el
/// carrito justo despues de confirmar la orden que lo consumio.
///
/// A diferencia de <c>ClienteInventarioHttp</c> de Despacho (que deja subir la
/// excepcion porque su llamado si es indispensable), aca la falla se traga y
/// solo se registra un warning: vaciar el carrito es una cortesia de UX, no
/// una garantia de negocio. La orden ya se confirmo y persistio antes de este
/// llamado (ver <see cref="Application.Ordenes.ServicioConfirmacionOrden.ConfirmarOrdenAsync" />);
/// si Auth no responde, el cliente puede ver items ya comprados en su carrito
/// hasta la proxima vez que lo toque, pero eso nunca debe tumbar ni revertir
/// la confirmacion de la orden.
/// </summary>
public sealed class ClienteCarritoHttp : IClienteCarrito
{
    private readonly HttpClient _http;
    private readonly ILogger<ClienteCarritoHttp> _logger;

    public ClienteCarritoHttp(HttpClient http, ILogger<ClienteCarritoHttp> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task VaciarAsync(string clienteId, CancellationToken cancelacion)
    {
        try
        {
            var respuesta = await _http.DeleteAsync(
                $"/interno/carrito/{Uri.EscapeDataString(clienteId)}", cancelacion);

            if (!respuesta.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "No se pudo vaciar el carrito de {ClienteId} tras confirmar la orden: Auth respondio {StatusCode}.",
                    clienteId, (int)respuesta.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(
                ex, "No se pudo contactar a Auth para vaciar el carrito de {ClienteId} tras confirmar la orden.",
                clienteId);
        }
    }
}