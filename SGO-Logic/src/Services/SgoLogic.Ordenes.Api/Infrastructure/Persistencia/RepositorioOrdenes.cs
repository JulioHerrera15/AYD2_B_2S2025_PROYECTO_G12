using Microsoft.EntityFrameworkCore;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia;

/// <summary>
/// Implementacion de <see cref="IRepositorioOrdenes" /> sobre
/// <see cref="OrdenesDbContext" />. No llama a SaveChanges: eso es tarea de la
/// unidad de trabajo.
/// </summary>
public sealed class RepositorioOrdenes : IRepositorioOrdenes
{
    private readonly OrdenesDbContext _contexto;

    public RepositorioOrdenes(OrdenesDbContext contexto)
    {
        _contexto = contexto;
    }

    public Task<Orden?> ObtenerPorIdAsync(string id, CancellationToken cancelacion) =>
        _contexto.Ordenes
            .Include(orden => orden.Lineas)
            .Include(orden => orden.Transiciones)
            .FirstOrDefaultAsync(orden => orden.Id == id, cancelacion);

    public Task<Orden?> ObtenerPorNumeroAsync(string numeroOrden, CancellationToken cancelacion) =>
        _contexto.Ordenes
            .Include(orden => orden.Lineas)
            .Include(orden => orden.Transiciones)
            .FirstOrDefaultAsync(orden => orden.NumeroOrden == numeroOrden, cancelacion);

    public async Task AgregarAsync(Orden orden, CancellationToken cancelacion)
    {
        await _contexto.Ordenes.AddAsync(orden, cancelacion);
    }

    public Task<IReadOnlyList<Orden>> ObtenerConAlertaFiscalAsync(CancellationToken cancelacion) =>
        _contexto.Ordenes
            .Include(orden => orden.Lineas)
            .Include(orden => orden.Transiciones)
            .Where(orden => orden.AlertaFiscal)
            .OrderByDescending(orden => orden.AlertaFiscalEn)
            .ToListAsync(cancelacion)
            .ContinueWith(t => (IReadOnlyList<Orden>)t.Result, cancelacion);

    public Task<IReadOnlyList<Devolucion>> ObtenerDevolucionesPorOrdenAsync(string ordenId, CancellationToken cancelacion) =>
        _contexto.Devoluciones
            .Include(d => d.Lineas)
            .Where(d => d.OrdenOriginalId == ordenId)
            .OrderBy(d => d.RegistradaEn)
            .ToListAsync(cancelacion)
            .ContinueWith(t => (IReadOnlyList<Devolucion>)t.Result, cancelacion);

    public Task<Devolucion?> ObtenerDevolucionPorIdAsync(string devolucionId, CancellationToken cancelacion) =>
        _contexto.Devoluciones
            .Include(d => d.Lineas)
            .FirstOrDefaultAsync(d => d.Id == devolucionId, cancelacion);

    public async Task AgregarDevolucionAsync(Devolucion devolucion, CancellationToken cancelacion)
    {
        await _contexto.Devoluciones.AddAsync(devolucion, cancelacion);
    }
}
