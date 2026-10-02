using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SgoLogic.Credito.Api.Application;

namespace SgoLogic.Credito.Api.Controllers;

/// <summary>
/// Oferta de credito y "Mi credito" del cliente autenticado. El cliente sale SIEMPRE
/// del claim email del JWT (igual que Pagos), nunca de la ruta, la query ni el
/// cuerpo: un clienteId en la query se ignora. Ninguna respuesta trae el puntaje.
/// </summary>
[ApiController]
[Authorize]
[Route("api/credito")]
public sealed class CreditoController : ControllerBase
{
    private readonly ServicioElegibilidad _servicio;

    public CreditoController(ServicioElegibilidad servicio)
    {
        _servicio = servicio;
    }

    /// <summary>Oferta para el monto de la compra (opcional). 400 si el monto es 0 o negativo.</summary>
    [HttpGet("elegibilidad")]
    [ProducesResponseType<OfertaCreditoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Elegibilidad([FromQuery] decimal? monto, CancellationToken cancelacion)
    {
        var clienteId = ClienteId();
        if (clienteId is null)
        {
            return SinCliente();
        }

        if (monto is <= 0m)
        {
            return Problema(StatusCodes.Status400BadRequest, "Monto invalido", "El monto debe ser mayor que 0.", "monto_invalido");
        }

        return Ok(await _servicio.ConsultarAsync(clienteId, monto, cancelacion));
    }

    /// <summary>Limite, disponible, utilizado y factores del perfil explicados.</summary>
    [HttpGet("mi-credito")]
    [ProducesResponseType<MiCreditoDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MiCredito(CancellationToken cancelacion)
    {
        var clienteId = ClienteId();
        if (clienteId is null)
        {
            return SinCliente();
        }

        return Ok(await _servicio.ObtenerMiCreditoAsync(clienteId, cancelacion));
    }

    /// <summary>Email del claim del JWT, normalizado. Null si el token no lo trae.</summary>
    private string? ClienteId()
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
        return string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    }

    private ObjectResult SinCliente() =>
        Problema(StatusCodes.Status401Unauthorized, "Sesion invalida", "El token no identifica a un cliente.", "cliente_no_identificado");

    private ObjectResult Problema(int status, string titulo, string detalle, string codigo)
    {
        var problema = new ProblemDetails
        {
            Title = titulo,
            Detail = detalle,
            Status = status
        };
        problema.Extensions["codigo"] = codigo;
        return StatusCode(status, problema);
    }
}
