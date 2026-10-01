using Microsoft.AspNetCore.Mvc;
using SgoLogic.Auth.Api.Application;

namespace SgoLogic.Auth.Api.Controllers;

/// <summary>
/// Endpoint de servicio a servicio: lo llama Ordenes al confirmar una orden,
/// para vaciar el carrito del cliente dueño de esa orden (no el propio carrito
/// del llamador, como hacen los endpoints de <see cref="CarritoController" />).
///
/// A proposito FUERA de /api/carrito y /carrito: el Gateway proxya ambos
/// prefijos hacia Auth sin exigir JWT (Gateway:ExigirJwt=false por defecto), y
/// este endpoint recibe el identificador de OTRO usuario sin validar la
/// identidad de quien llama. Bajo esos prefijos quedaria alcanzable desde
/// afuera para vaciar el carrito de cualquiera con solo adivinar su
/// clienteId. El Gateway no tiene ninguna ruta configurada para "/interno/",
/// asi que solo es alcanzable dentro de la red de Docker Compose, llamando
/// directo a auth-api:8080 (mismo patron que Despacho usa para llamar a
/// Inventario via IClienteInventario).
/// </summary>
[ApiController]
[Route("interno/carrito")]
public sealed class CarritoInternoController : ControllerBase
{
    private readonly ICarritoService _carrito;

    public CarritoInternoController(ICarritoService carrito)
    {
        _carrito = carrito;
    }

    /// <summary>Vacia el carrito de un cliente. Idempotente: si ya estaba vacio o no existia, no falla.</summary>
    [HttpDelete("{clienteId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Vaciar(string clienteId, CancellationToken ct)
    {
        await _carrito.VaciarAsync(clienteId, ct);
        return NoContent();
    }
}