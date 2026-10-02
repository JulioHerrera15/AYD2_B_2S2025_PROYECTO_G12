using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SgoLogic.Pagos.Api.Application.Tokens;

namespace SgoLogic.Pagos.Api.Controllers;

/// <summary>
/// Tokenizacion interna de tarjetas nuevas (A3). Sin autenticacion obligatoria: los
/// invitados tambien pagan. Con JWT el token queda a nombre del cliente (claim email)
/// y guardar=true se respeta; sin JWT guardar se ignora.
///
/// No llama a PayKlik. El cuerpo trae PAN y CVV: no se registra, y la respuesta solo
/// devuelve token, marca, ultimos cuatro y vencimiento del token.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/pagos/tokens")]
public sealed class TokensController : ControllerBase
{
    private readonly ServicioTokensPago _servicio;

    public TokensController(ServicioTokensPago servicio)
    {
        _servicio = servicio;
    }

    /// <summary>201 con el token; 422 con {errores:{campo:mensaje}} si la tarjeta no es valida.</summary>
    [HttpPost]
    [ProducesResponseType<TokenPagoDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Crear([FromBody] SolicitudTokenPago? solicitud, CancellationToken cancelacion)
    {
        var resultado = await _servicio.TokenizarAsync(
            solicitud ?? new SolicitudTokenPago(null, null, null, null, null),
            ClienteId(),
            cancelacion);

        return resultado.Exito
            ? StatusCode(StatusCodes.Status201Created, resultado.Token)
            : UnprocessableEntity(new { errores = resultado.Errores });
    }

    private string? ClienteId()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
        return string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    }
}
