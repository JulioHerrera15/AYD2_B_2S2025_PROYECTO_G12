using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SgoLogic.Auth.Api.Application;
using System.Security.Claims;

namespace SgoLogic.Auth.Api.Controllers;

/// <summary>
/// Endpoints de carrito unificado. El carrito se identifica por:
///   - clienteId (de la claim "sub" del JWT) cuando el usuario esta autenticado
///   - sesionId (header X-Session-Id) cuando es invitado
/// Ambos carritos comparten la misma clave de Redis una vez asociados con
/// POST /api/carrito/asociar, que migra guest:{sesionId} → cart:{clienteId}.
///
/// Agregar y actualizar toman precio y descripcion del catalogo de Inventario (404 si
/// el SKU no existe o esta inactivo): lo que mande el cliente se ignora.
///
/// Agregar, actualizar, quitar, asociar y renovar pasan primero por Inventario,
/// que retiene el stock. Si Inventario responde 409 se devuelve 409 con el mismo
/// cuerpo; si no responde, 503. En ambos casos el carrito no cambia.
/// </summary>
[ApiController]
[Route("api/carrito")]
public sealed class CarritoController : ControllerBase
{
    private readonly ICarritoService _carrito;

    public CarritoController(ICarritoService carrito)
    {
        _carrito = carrito;
    }

    /// <summary>Devuelve el carrito del usuario autenticado o del invitado.</summary>
    [HttpGet]
    [ProducesResponseType<CarritoDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Obtener(CancellationToken ct)
    {
        var id = ResolverIdentificador();
        var carrito = await _carrito.ObtenerAsync(id, ct);
        return Ok(carrito);
    }

    /// <summary>Agrega o suma cantidad de un item al carrito.</summary>
    [HttpPost("items")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> AgregarItem([FromBody] AgregarItemRequest request, CancellationToken ct) =>
        OperarAsync(async () =>
        {
            var id = ResolverIdentificador();
            await _carrito.AgregarItemAsync(id, request, ct);
            return NoContent();
        });

    /// <summary>Actualiza la cantidad de un item del carrito por SKU.</summary>
    [HttpPut("items/{sku}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ActualizarCantidad(string sku, [FromBody] ActualizarCantidadRequest request, CancellationToken ct)
    {
        if (request.Cantidad < 0)
            return BadRequest(new ProblemDetails { Title = "Solicitud inválida", Detail = "La cantidad no puede ser negativa." });

        return await OperarAsync(async () =>
        {
            var id = ResolverIdentificador();
            await _carrito.ActualizarCantidadItemAsync(id, sku, request.Cantidad, request.UbicacionPreferidaId, ct);
            return NoContent();
        });
    }

    /// <summary>Elimina un item del carrito por SKU.</summary>
    [HttpDelete("items/{sku}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> EliminarItem(string sku, CancellationToken ct) =>
        OperarAsync(async () =>
        {
            var id = ResolverIdentificador();
            await _carrito.EliminarItemAsync(id, sku, ct);
            return NoContent();
        });

    /// <summary>
    /// Extiende la retencion de stock de todo el carrito y reactiva las lineas
    /// vencidas donde todavia haya stock. Devuelve el carrito actualizado; un item que
    /// no se pudo reactivar queda con estadoReserva EXPIRADA.
    /// </summary>
    [HttpPost("reserva/renovar")]
    [ProducesResponseType<CarritoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> RenovarReserva(CancellationToken ct) =>
        OperarAsync(async () =>
        {
            var id = ResolverIdentificador();
            return Ok(await _carrito.RenovarReservaAsync(id, ct));
        });

    /// <summary>
    /// Asocia el carrito de invitado (guest:{sesionId}) al carrito de la cuenta
    /// (cart:{clienteId}) del usuario autenticado. Debe llamarse justo despues del
    /// login cuando el usuario tenia un carrito de invitado.
    /// </summary>
    [HttpPost("asociar")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Asociar([FromBody] AsociarCarritoRequest request, CancellationToken ct)
    {
        var clienteId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException("No se pudo obtener el clienteId del token.");

        return await OperarAsync(async () =>
        {
            await _carrito.AsociarAsync(request.SesionId, clienteId, ct);
            return NoContent();
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Traduce los rechazos de Inventario: 409 pasa con el mismo cuerpo (sku,
    /// disponible, mensaje y sucursal cercana) y la falta de respuesta es 503.
    /// </summary>
    private async Task<IActionResult> OperarAsync(Func<Task<IActionResult>> operacion)
    {
        try
        {
            return await operacion();
        }
        catch (ExcepcionInventarioRechazo ex)
        {
            return new ContentResult
            {
                StatusCode = ex.Status,
                Content = ex.Cuerpo,
                ContentType = string.IsNullOrWhiteSpace(ex.ContentType) ? "application/json" : ex.ContentType
            };
        }
        catch (ExcepcionProductoNoEncontrado ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Producto no encontrado",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (ExcepcionInventarioNoDisponible)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "Inventario no disponible",
                Detail = "No pudimos confirmar la disponibilidad del producto en este momento. Tu carrito no se modifico; intenta de nuevo en unos minutos.",
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Solicitud invalida", Detail = ex.Message });
        }
    }

    private string ResolverIdentificador()
    {
        // Usuario autenticado → usa sub del JWT
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!string.IsNullOrEmpty(sub))
            return sub;

        // Invitado o consulta POS → usa header X-Session-Id
        var sesion = Request.Headers["X-Session-Id"].FirstOrDefault();
        if (!string.IsNullOrEmpty(sesion))
            return sesion.Trim();

        // Si no hay ni JWT ni sesion, generar un ID temporal (no persistido)
        return "guest:" + Guid.NewGuid().ToString("N");
    }
}
