using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SgoLogic.Pagos.Api.Application.Tarjetas;
using SgoLogic.Pagos.Api.Application.Tokens;

namespace SgoLogic.Pagos.Api.Controllers;

/// <summary>
/// Tarjetas guardadas del cliente autenticado. El cliente sale SIEMPRE del claim
/// email del JWT (el mismo identificador que usa Ordenes), nunca de la ruta ni del
/// cuerpo: no hay forma de listar, usar o borrar tarjetas de otro.
///
/// Ningun endpoint devuelve la referencia de PayKlik. El CVV de POST {id}/token
/// queda cifrado en la boveda transitoria hasta el cobro: no se registra.
/// </summary>
[ApiController]
[Authorize]
[Route("api/pagos/tarjetas")]
public sealed class TarjetasController : ControllerBase
{
    private readonly ServicioTarjetasGuardadas _servicio;
    private readonly ServicioTokensPago _tokens;

    public TarjetasController(ServicioTarjetasGuardadas servicio, ServicioTokensPago tokens)
    {
        _servicio = servicio;
        _tokens = tokens;
    }

    /// <summary>Tarjetas activas del cliente, con la propiedad calculada vencida.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TarjetaGuardadaDto>>> Listar(CancellationToken cancelacion)
    {
        var clienteId = ClienteId();
        if (clienteId is null)
        {
            return SinCliente();
        }

        return Ok(await _servicio.ListarAsync(clienteId, cancelacion));
    }

    /// <summary>Guarda la tarjeta; si ya existe la misma activa, actualiza su referencia (200 en vez de 201).</summary>
    [HttpPost]
    public async Task<ActionResult<TarjetaGuardadaDto>> Guardar(
        [FromBody] GuardarTarjetaRequest solicitud,
        CancellationToken cancelacion)
    {
        var clienteId = ClienteId();
        if (clienteId is null)
        {
            return SinCliente();
        }

        try
        {
            var (tarjeta, estado) = await _servicio.GuardarAsync(clienteId, solicitud, cancelacion);
            return estado == EstadoGuardarTarjeta.Creada
                ? StatusCode(StatusCodes.Status201Created, tarjeta)
                : Ok(tarjeta);
        }
        catch (ArgumentException ex)
        {
            return Problema(StatusCodes.Status400BadRequest, "Tarjeta invalida", ex.Message, "tarjeta_invalida");
        }
    }

    /// <summary>Baja logica de una tarjeta del cliente.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken cancelacion)
    {
        var clienteId = ClienteId();
        if (clienteId is null)
        {
            return SinCliente();
        }

        return await _servicio.EliminarAsync(clienteId, id, cancelacion)
            ? NoContent()
            : NoEncontrada();
    }

    /// <summary>
    /// Token de la boveda transitoria para pagar con la tarjeta guardada (no llama a
    /// PayKlik). Misma forma que POST /api/pagos/tokens. 404 si no es del cliente, 422
    /// con {errores:{campo:mensaje}} si esta vencida o el CVV no es valido para la marca.
    /// </summary>
    [HttpPost("{id:guid}/token")]
    public async Task<IActionResult> Token(
        Guid id,
        [FromBody] SolicitarTokenTarjetaRequest? solicitud,
        CancellationToken cancelacion)
    {
        var clienteId = ClienteId();
        if (clienteId is null)
        {
            return SinCliente();
        }

        var resultado = await _tokens.TokenizarTarjetaGuardadaAsync(clienteId, id, solicitud?.Cvv, cancelacion);

        return resultado.Estado switch
        {
            EstadoTokenTarjetaGuardada.Emitido => StatusCode(StatusCodes.Status201Created, resultado.Token),
            EstadoTokenTarjetaGuardada.NoEncontrada => NoEncontrada(),
            EstadoTokenTarjetaGuardada.Vencida => UnprocessableEntity(new { errores = new Dictionary<string, string> { ["anioExp"] = resultado.Mensaje! } }),
            _ => UnprocessableEntity(new { errores = new Dictionary<string, string> { ["cvv"] = resultado.Mensaje! } })
        };
    }

    /// <summary>Email del claim del JWT, normalizado. Null si el token no lo trae.</summary>
    private string? ClienteId()
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
        return string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    }

    private ObjectResult SinCliente() =>
        Problema(StatusCodes.Status401Unauthorized, "Sesion invalida", "El token no identifica a un cliente.", "cliente_no_identificado");

    private ObjectResult NoEncontrada() =>
        Problema(StatusCodes.Status404NotFound, "Tarjeta no encontrada", "La tarjeta no existe.", "tarjeta_no_encontrada");

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
