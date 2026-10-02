using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SgoLogic.Contracts.Eventos.IntegracionErp;
using SgoLogic.Credito.Api.Application;

namespace SgoLogic.Credito.Api.Controllers;

/// <summary>
/// Entrada interna de historiales para demo y pruebas mientras no exista el worker
/// del ERP. Solo se registra con Credito:EndpointsInternos=true (ver
/// <see cref="SinControladoresInternos" />) y el gateway no la enruta. Devuelve el
/// perfil con puntaje: no es una respuesta para el cliente.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("interno/credito/historiales")]
public sealed class CreditoInternoController : ControllerBase
{
    private readonly ServicioPerfilCrediticio _servicio;

    public CreditoInternoController(ServicioPerfilCrediticio servicio)
    {
        _servicio = servicio;
    }

    /// <summary>Aplica el historial (mismo cuerpo que el data de HistorialClienteErpActualizado).</summary>
    [HttpPost]
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
}
