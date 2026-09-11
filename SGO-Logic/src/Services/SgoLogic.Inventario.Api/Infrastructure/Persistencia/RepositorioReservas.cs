using Microsoft.EntityFrameworkCore;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia;

/// <summary>
/// Implementacion de <see cref="IRepositorioReservas" /> sobre
/// <see cref="InventarioDbContext" />. No llama a SaveChanges: eso es tarea de la
/// unidad de trabajo.
/// </summary>
public sealed class RepositorioReservas : IRepositorioReservas
{
    private readonly InventarioDbContext _contexto;

    public RepositorioReservas(InventarioDbContext contexto)
    {
        _contexto = contexto;
    }

    public Task<Reserva?> ObtenerPorOrdenAsync(string ordenId, CancellationToken cancelacion) =>
        _contexto.Reservas
            .Include(reserva => reserva.Lineas)
            .FirstOrDefaultAsync(reserva => reserva.OrdenId == ordenId, cancelacion);

    public Task<Reserva?> ObtenerPorIdAsync(string reservaId, CancellationToken cancelacion) =>
        _contexto.Reservas
            .Include(reserva => reserva.Lineas)
            .FirstOrDefaultAsync(reserva => reserva.Id == reservaId, cancelacion);

    public async Task AgregarAsync(Reserva reserva, CancellationToken cancelacion) =>
        await _contexto.Reservas.AddAsync(reserva, cancelacion);
}
