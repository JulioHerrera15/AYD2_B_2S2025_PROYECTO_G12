using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SgoLogic.ConsultaEstado.Api.Application;
using SgoLogic.ConsultaEstado.Api.Domain;
using SgoLogic.ConsultaEstado.Api.Infrastructure;

namespace SgoLogic.ConsultaEstado.Api.Controllers;

/// <summary>
/// Endpoints de gestion de incidencias de ordenes (HU-021).
/// Las incidencias viven completamente dentro de ConsultaEstado — no generan
/// eventos hacia otros contextos.
/// </summary>
[ApiController]
[Route("api/incidencias")]
public sealed class IncidenciasController : ControllerBase
{
    private readonly ConsultaEstadoDbContext _contexto;

    public IncidenciasController(ConsultaEstadoDbContext contexto)
    {
        _contexto = contexto;
    }

    /// <summary>Crea una incidencia para una orden.</summary>
    [HttpPost]
    [ProducesResponseType<IncidenciaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Crear([FromBody] CrearIncidenciaRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.OrdenId))
            return BadRequest(new ProblemDetails { Title = "Solicitud invalida", Detail = "OrdenId es requerido." });

        if (string.IsNullOrWhiteSpace(request.Descripcion))
            return BadRequest(new ProblemDetails { Title = "Solicitud invalida", Detail = "Descripcion es requerida." });

        var incidencia = new Incidencia
        {
            Id = Guid.NewGuid(),
            OrdenId = request.OrdenId,
            Descripcion = request.Descripcion,
            Estado = "ABIERTA",
            CreadaEn = DateTime.UtcNow
        };

        _contexto.Incidencias.Add(incidencia);
        await _contexto.SaveChangesAsync(ct);

        var dto = new IncidenciaDto(
            incidencia.Id,
            incidencia.Estado,
            incidencia.Descripcion,
            incidencia.Resolucion,
            incidencia.CreadaEn,
            incidencia.ActualizadaEn);

        return StatusCode(StatusCodes.Status201Created, dto);
    }

    /// <summary>Actualiza el estado o resolucion de una incidencia.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<IncidenciaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Actualizar(
        Guid id,
        [FromBody] ActualizarIncidenciaRequest request,
        CancellationToken ct)
    {
        var incidencia = await _contexto.Incidencias.FindAsync(new object[] { id }, ct);
        if (incidencia is null)
            return NotFound(new ProblemDetails { Title = "No encontrado", Detail = $"Incidencia {id} no encontrada." });

        if (!string.IsNullOrWhiteSpace(request.Descripcion))
            incidencia.Descripcion = request.Descripcion;

        if (!string.IsNullOrWhiteSpace(request.Estado))
            incidencia.Estado = request.Estado;

        if (request.Resolucion is not null)
            incidencia.Resolucion = request.Resolucion;

        incidencia.ActualizadaEn = DateTime.UtcNow;
        await _contexto.SaveChangesAsync(ct);

        return Ok(new IncidenciaDto(
            incidencia.Id,
            incidencia.Estado,
            incidencia.Descripcion,
            incidencia.Resolucion,
            incidencia.CreadaEn,
            incidencia.ActualizadaEn));
    }
}
