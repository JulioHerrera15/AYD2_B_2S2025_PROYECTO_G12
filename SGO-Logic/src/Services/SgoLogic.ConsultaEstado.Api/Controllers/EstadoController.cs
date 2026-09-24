using Microsoft.AspNetCore.Mvc;
using SgoLogic.ConsultaEstado.Api.Application;

namespace SgoLogic.ConsultaEstado.Api.Controllers;

/// <summary>
/// Endpoints de consulta de estado de ordenes.
/// EAC-02: respuesta en menos de 5 segundos.
/// </summary>
[ApiController]
[Route("api/estado")]
public sealed class EstadoController : ControllerBase
{
    private readonly IConsultaEstadoService _servicio;

    public EstadoController(IConsultaEstadoService servicio)
    {
        _servicio = servicio;
    }

    /// <summary>
    /// Devuelve el estado actual de una orden incluyendo sus incidencias.
    /// Respuesta desde Redis (cache caliente) con fallback a PostgreSQL.
    /// </summary>
    [HttpGet("ordenes/{ordenId}")]
    [ProducesResponseType<OrdenEstadoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObtenerOrden(string ordenId, CancellationToken ct)
    {
        var resultado = await _servicio.ObtenerOrdenAsync(ordenId, ct);
        return resultado is null
            ? NotFound(new ProblemDetails { Title = "No encontrado", Detail = $"Orden {ordenId} no encontrada." })
            : Ok(resultado);
    }

    /// <summary>
    /// Devuelve el historial de ordenes de un cliente, paginado.
    /// </summary>
    [HttpGet("clientes/{clienteId}/ordenes")]
    [ProducesResponseType<HistorialOrdenesDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ObtenerHistorial(
        string clienteId,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = 20,
        CancellationToken ct = default)
    {
        var resultado = await _servicio.ObtenerHistorialAsync(clienteId, pagina, tamano, ct);
        return Ok(resultado);
    }
}
