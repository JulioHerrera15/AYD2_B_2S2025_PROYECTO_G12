using Microsoft.AspNetCore.Mvc;
using SgoLogic.Auditoria.Worker.Application;

namespace SgoLogic.Auditoria.Worker.Controllers;

/// <summary>
/// API de solo lectura de la bitacora de auditoria. Ningun endpoint aca escribe:
/// la unica escritura del servicio la hace AuditoriaConsumidor al consumir de
/// RabbitMQ.
/// </summary>
[ApiController]
[Route("auditoria")]
public sealed class AuditoriaController : ControllerBase
{
    private readonly IAuditoriaConsultaService _servicio;

    public AuditoriaController(IAuditoriaConsultaService servicio)
    {
        _servicio = servicio;
    }

    /// <summary>Toda la evidencia de una transaccion, en orden cronologico. EAC-03: menos de 1 segundo.</summary>
    [HttpGet("transacciones/{transactionId}")]
    [ProducesResponseType<PaginaResultado<EventoAuditoriaDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PaginaResultado<EventoAuditoriaDto>>> ObtenerPorTransaccion(
        string transactionId,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = 50,
        CancellationToken cancelacion = default)
    {
        return Ok(await _servicio.ObtenerPorTransaccionAsync(transactionId, pagina, tamano, cancelacion));
    }

    /// <summary>Todos los eventos de un agregado (por ejemplo, una orden).</summary>
    [HttpGet("agregados/{aggregateId}")]
    [ProducesResponseType<PaginaResultado<EventoAuditoriaDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PaginaResultado<EventoAuditoriaDto>>> ObtenerPorAgregado(
        string aggregateId,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = 50,
        CancellationToken cancelacion = default)
    {
        return Ok(await _servicio.ObtenerPorAgregadoAsync(aggregateId, pagina, tamano, cancelacion));
    }
}