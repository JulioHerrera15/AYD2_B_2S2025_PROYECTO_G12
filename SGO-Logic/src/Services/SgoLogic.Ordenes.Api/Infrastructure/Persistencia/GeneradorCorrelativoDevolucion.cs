using Microsoft.EntityFrameworkCore;
using SgoLogic.Ordenes.Api.Application.Ordenes;

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia;

/// <summary>
/// Implementacion de <see cref="IGeneradorCorrelativoDevolucion" /> sobre la secuencia
/// de PostgreSQL <c>ordenes.devolucion_correlativo</c> (creada por la migracion). Cada
/// llamada consume un valor de la secuencia; nextval no participa de la
/// transaccion, asi que un rollback posterior solo deja un hueco en la
/// numeracion, nunca un id repetido.
/// </summary>
public sealed class GeneradorCorrelativoDevolucion : IGeneradorCorrelativoDevolucion
{
    private readonly OrdenesDbContext _contexto;

    public GeneradorCorrelativoDevolucion(OrdenesDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<long> SiguienteAsync(CancellationToken cancelacion) =>
        await _contexto.Database
            .SqlQueryRaw<long>("SELECT nextval('ordenes.devolucion_correlativo') AS \"Value\"")
            .SingleAsync(cancelacion);
}
