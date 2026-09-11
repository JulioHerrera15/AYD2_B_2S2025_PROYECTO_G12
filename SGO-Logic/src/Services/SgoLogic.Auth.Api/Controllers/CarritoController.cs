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
    public async Task<IActionResult> AgregarItem([FromBody] AgregarItemRequest request, CancellationToken ct)
    {
        try
        {
            var id = ResolverIdentificador();
            await _carrito.AgregarItemAsync(id, request, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Solicitud invalida", Detail = ex.Message });
        }
    }

    /// <summary>Elimina un item del carrito por SKU.</summary>
    [HttpDelete("items/{sku}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> EliminarItem(string sku, CancellationToken ct)
    {
        var id = ResolverIdentificador();
        await _carrito.EliminarItemAsync(id, sku, ct);
        return NoContent();
    }

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

        await _carrito.AsociarAsync(request.SesionId, clienteId, ct);
        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string ResolverIdentificador()
    {
        // Usuario autenticado → usa sub del JWT
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!string.IsNullOrEmpty(sub))
            return sub;

        // Invitado → usa header X-Session-Id
        var sesion = Request.Headers["X-Session-Id"].FirstOrDefault();
        if (!string.IsNullOrEmpty(sesion))
            return "guest:" + sesion;

        // Si no hay ni JWT ni sesion, generar un ID temporal (no persistido)
        return "guest:" + Guid.NewGuid().ToString("N");
    }
}
