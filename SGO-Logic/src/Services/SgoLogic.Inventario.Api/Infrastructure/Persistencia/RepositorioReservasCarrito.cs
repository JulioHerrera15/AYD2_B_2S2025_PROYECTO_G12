using Microsoft.EntityFrameworkCore;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia;

/// <summary>
/// Implementacion de <see cref="IRepositorioReservasCarrito" /> sobre
/// <see cref="InventarioDbContext" />. No llama a SaveChanges: eso es tarea de la
/// unidad de trabajo.
/// </summary>
public sealed class RepositorioReservasCarrito : IRepositorioReservasCarrito
{
    private readonly InventarioDbContext _contexto;

    public RepositorioReservasCarrito(InventarioDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<IReadOnlyList<ReservaCarrito>> ListarVigentesPorCarritoAsync(
        string carritoId,
        CancellationToken cancelacion) =>
        await _contexto.ReservasCarrito
            .Where(retencion => retencion.CarritoId == carritoId
                && (retencion.Estado == EstadoReservaCarrito.ACTIVA || retencion.Estado == EstadoReservaCarrito.EXPIRADA))
            .OrderBy(retencion => retencion.CreadaEn)
            .ToListAsync(cancelacion);

    public async Task<IReadOnlyList<ReservaCarrito>> ListarActivasVencidasAsync(
        DateTime ahora,
        int limite,
        CancellationToken cancelacion)
    {
        var corte = ahora.ToUniversalTime();

        return await _contexto.ReservasCarrito
            .Where(retencion => retencion.Estado == EstadoReservaCarrito.ACTIVA && retencion.ExpiraEn <= corte)
            .OrderBy(retencion => retencion.ExpiraEn)
            .Take(limite)
            .ToListAsync(cancelacion);
    }

    public Task<ReservaCarrito?> ObtenerPorIdAsync(Guid id, CancellationToken cancelacion) =>
        _contexto.ReservasCarrito.FirstOrDefaultAsync(retencion => retencion.Id == id, cancelacion);

    public async Task AgregarAsync(ReservaCarrito retencion, CancellationToken cancelacion) =>
        await _contexto.ReservasCarrito.AddAsync(retencion, cancelacion);
}
