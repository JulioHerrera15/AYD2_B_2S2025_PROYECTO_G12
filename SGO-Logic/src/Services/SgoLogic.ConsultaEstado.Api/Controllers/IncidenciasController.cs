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

    /// <summary>Lista incidencias, opcionalmente filtradas por OrdenId o Estado.</summary>
    [HttpGet]
    [ProducesResponseType<List<IncidenciaDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(
        [FromQuery] string? ordenId,
        [FromQuery] string? estado,
        CancellationToken ct)
    {
        var query = _contexto.Incidencias.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(ordenId))
            query = query.Where(i => i.OrdenId == ordenId.Trim());

        if (!string.IsNullOrWhiteSpace(estado))
            query = query.Where(i => i.Estado == estado.Trim().ToUpperInvariant());

        var items = await query
            .OrderByDescending(i => i.CreadaEn)
            .Take(100)
            .Select(i => new IncidenciaDto(
                i.Id,
                i.Estado,
                i.Descripcion,
                i.Resolucion,
                i.CreadaEn,
                i.ActualizadaEn))
            .ToListAsync(ct);

        return Ok(items);
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
