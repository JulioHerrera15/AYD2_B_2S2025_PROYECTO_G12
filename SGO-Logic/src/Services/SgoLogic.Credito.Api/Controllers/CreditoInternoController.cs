using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SgoLogic.Contracts.Eventos.IntegracionErp;
using SgoLogic.Credito.Api.Application;

namespace SgoLogic.Credito.Api.Controllers;

/// <summary>
/// Endpoints internos para demo y pruebas: entrada de historiales mientras no exista
/// el worker del ERP, y reinicio de cuentas y decisiones. Solo se registran con
/// Credito:EndpointsInternos=true (ver <see cref="SinControladoresInternos" />) y el
/// gateway no los enruta. Devuelven datos internos: no son respuestas para el cliente.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("interno/credito")]
public sealed class CreditoInternoController : ControllerBase
{
    private readonly ServicioPerfilCrediticio _servicio;
    private readonly ServicioDemoCredito _demo;

    public CreditoInternoController(ServicioPerfilCrediticio servicio, ServicioDemoCredito demo)
    {
        _servicio = servicio;
        _demo = demo;
    }

    /// <summary>Aplica el historial (mismo cuerpo que el data de HistorialClienteErpActualizado).</summary>
    [HttpPost("historiales")]
    [ProducesResponseType<ResultadoAplicacionHistorial>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Aplicar([FromBody] HistorialClienteErpActualizadoData? datos, CancellationToken cancelacion)
    {
        if (datos is null)
        {
            return BadRequest(new ProblemDetails { Title = "Historial invalido", Detail = "El cuerpo es obligatorio.", Status = 400 });
        }

        try
        {
            return Ok(await _servicio.AplicarHistorialAsync(datos, cancelacion));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Historial invalido", Detail = ex.Message, Status = 400 });
        }
    }

    /// <summary>
    /// Borra cuentas por cobrar y decisiones y deja el saldo utilizado de todas las
    /// lineas en 0, sin tocar perfiles. Para repetir pruebas y demos.
    /// </summary>
    [HttpPost("demo/reiniciar")]
    [ProducesResponseType<ResultadoReinicioDemo>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReiniciarDemo(CancellationToken cancelacion) =>
        Ok(await _demo.ReiniciarAsync(cancelacion));
}
