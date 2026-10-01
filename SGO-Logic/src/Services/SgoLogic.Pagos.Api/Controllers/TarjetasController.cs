using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SgoLogic.Pagos.Api.Application.Tarjetas;

namespace SgoLogic.Pagos.Api.Controllers;

/// <summary>
/// Tarjetas guardadas del cliente autenticado. El cliente sale SIEMPRE del claim
/// email del JWT (el mismo identificador que usa Ordenes), nunca de la ruta ni del
/// cuerpo: no hay forma de listar, usar o borrar tarjetas de otro.
///
/// Ningun endpoint devuelve la referencia de PayKlik, y el CVV de
/// POST {id}/token solo se reenvia a PayKlik: no se guarda ni se registra.
/// </summary>
[ApiController]
[Authorize]
[Route("api/pagos/tarjetas")]
public sealed class TarjetasController : ControllerBase
{
    private readonly ServicioTarjetasGuardadas _servicio;

    public TarjetasController(ServicioTarjetasGuardadas servicio)
    {
        _servicio = servicio;
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
    /// Token de un solo uso de PayKlik para pagar con la tarjeta guardada. Misma forma
    /// que la tokenizacion directa. 404 si no es del cliente, 422 si esta vencida o el
    /// CVV no es valido, 410 si PayKlik ya no la reconoce (queda desactivada).
    /// </summary>
    [HttpPost("{id:guid}/token")]
    public async Task<ActionResult<TokenTarjetaDto>> Token(
        Guid id,
        [FromBody] SolicitarTokenTarjetaRequest solicitud,
        CancellationToken cancelacion)
    {
        var clienteId = ClienteId();
        if (clienteId is null)
        {
            return SinCliente();
        }

        var resultado = await _servicio.ObtenerTokenAsync(clienteId, id, solicitud?.Cvv, cancelacion);

        return resultado.Estado switch
        {
            EstadoTokenTarjeta.Emitido => StatusCode(StatusCodes.Status201Created, resultado.Token),
            EstadoTokenTarjeta.NoEncontrada => NoEncontrada(),
            EstadoTokenTarjeta.Vencida => Problema(StatusCodes.Status422UnprocessableEntity, "Tarjeta vencida", resultado.Mensaje!, "tarjeta_vencida"),
            EstadoTokenTarjeta.CvvInvalido => Problema(StatusCodes.Status422UnprocessableEntity, "Codigo de seguridad invalido", resultado.Mensaje!, "cvv_invalido"),
            EstadoTokenTarjeta.YaNoValida => Problema(StatusCodes.Status410Gone, "Tarjeta no valida", resultado.Mensaje!, "tarjeta_no_valida"),
            EstadoTokenTarjeta.RechazadaPorProveedor => Problema(StatusCodes.Status422UnprocessableEntity, "Tarjeta rechazada", resultado.Mensaje!, "tarjeta_rechazada"),
            _ => Problema(StatusCodes.Status503ServiceUnavailable, "PayKlik no disponible", resultado.Mensaje!, "proveedor_no_disponible")
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
