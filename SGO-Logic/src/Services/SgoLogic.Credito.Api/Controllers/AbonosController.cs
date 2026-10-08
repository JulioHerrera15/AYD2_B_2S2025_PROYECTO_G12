using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SgoLogic.Credito.Api.Application;

namespace SgoLogic.Credito.Api.Controllers;

/// <summary>
/// Pagos (abonos) del cliente a sus compras a credito. El cliente sale SIEMPRE del
/// claim email del JWT. El cobro es asincrono: crear o reintentar responde 202 y la
/// Web consulta el abono hasta que termina. Los errores traen en detail un texto para
/// el cliente y en codigo un identificador estable.
/// </summary>
[ApiController]
[Authorize]
[Route("api/credito/abonos")]
public sealed class AbonosController : ControllerBase
{
    private readonly ServicioAbonos _servicio;

    public AbonosController(ServicioAbonos servicio)
    {
        _servicio = servicio;
    }

    /// <summary>202 con el abono; 404 cuenta no encontrada; 409 abono_en_curso o cuenta_no_pagable; 422 monto_invalido.</summary>
    [HttpPost]
    [ProducesResponseType<AbonoDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Crear([FromBody] CrearAbonoRequest? solicitud, CancellationToken cancelacion)
    {
        var clienteId = ClienteId();
        if (clienteId is null)
        {
            return SinCliente();
        }

        var resultado = await _servicio.CrearAsync(
            clienteId, solicitud?.CuentaId, solicitud?.Monto ?? 0m, solicitud?.TokenMedioPago, cancelacion);
        return Responder(resultado);
    }

    [HttpGet("{id}")]
    [ProducesResponseType<AbonoDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Obtener(string id, CancellationToken cancelacion)
    {
        var clienteId = ClienteId();
        if (clienteId is null)
        {
            return SinCliente();
        }

        var resultado = await _servicio.ObtenerAsync(clienteId, id, cancelacion);
        return resultado.Exito ? Ok(resultado.Abono) : Responder(resultado);
    }

    /// <summary>Solo para un abono pendiente: mismo abono, token nuevo. 202, 404 o 409 abono_no_reintentable.</summary>
    [HttpPost("{id}/reintentar")]
    [ProducesResponseType<AbonoDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Reintentar(string id, [FromBody] ReintentarAbonoRequest? solicitud, CancellationToken cancelacion)
    {
        var clienteId = ClienteId();
        if (clienteId is null)
        {
            return SinCliente();
        }

        var resultado = await _servicio.ReintentarAsync(clienteId, id, solicitud?.TokenMedioPago, cancelacion);
        return Responder(resultado);
    }

    private IActionResult Responder(ResultadoAbono resultado)
    {
        if (resultado.Exito)
        {
            return StatusCode(StatusCodes.Status202Accepted, resultado.Abono);
        }

        var status = resultado.CodigoError switch
        {
            ErroresAbono.CuentaNoEncontrada or ErroresAbono.AbonoNoEncontrado => StatusCodes.Status404NotFound,
            ErroresAbono.AbonoEnCurso or ErroresAbono.CuentaNoPagable or ErroresAbono.AbonoNoReintentable => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        var problema = new ProblemDetails
        {
            Title = "No se pudo procesar el pago",
            Detail = resultado.MensajeError,
            Status = status
        };
        problema.Extensions["codigo"] = resultado.CodigoError;
        return StatusCode(status, problema);
    }

    private string? ClienteId()
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
        return string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    }

    private ObjectResult SinCliente()
    {
        var problema = new ProblemDetails
        {
            Title = "Sesion invalida",
            Detail = "El token no identifica a un cliente.",
            Status = StatusCodes.Status401Unauthorized
        };
        problema.Extensions["codigo"] = "cliente_no_identificado";
        return StatusCode(StatusCodes.Status401Unauthorized, problema);
    }
}
