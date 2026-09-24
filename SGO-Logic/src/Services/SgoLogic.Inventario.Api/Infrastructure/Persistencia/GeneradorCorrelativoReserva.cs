using Microsoft.EntityFrameworkCore;
using SgoLogic.Inventario.Api.Application.Inventario;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia;

/// <summary>
/// Implementacion de <see cref="IGeneradorCorrelativoReserva" /> sobre la secuencia
/// de PostgreSQL <c>inventario.reserva_correlativo</c> (creada por la migracion).
/// Cada llamada consume un valor; nextval no participa de la transaccion, asi que
/// un rollback posterior solo deja un hueco en la numeracion, nunca un id repetido.
/// </summary>
public sealed class GeneradorCorrelativoReserva : IGeneradorCorrelativoReserva
{
    private readonly InventarioDbContext _contexto;

    public GeneradorCorrelativoReserva(InventarioDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<long> SiguienteAsync(CancellationToken cancelacion) =>
        await _contexto.Database
            .SqlQueryRaw<long>("SELECT nextval('inventario.reserva_correlativo') AS \"Value\"")
            .SingleAsync(cancelacion);
}
