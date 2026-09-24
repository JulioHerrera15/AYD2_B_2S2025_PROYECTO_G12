using Microsoft.EntityFrameworkCore;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia;

/// <summary>
/// Implementacion de <see cref="IRepositorioMovimientos" /> sobre
/// <see cref="InventarioDbContext" />. No llama a SaveChanges: eso es tarea de la
/// unidad de trabajo, para que el asiento quede en la misma transaccion que el
/// cambio de cantidad fisica que lo origina.
/// </summary>
public sealed class RepositorioMovimientos : IRepositorioMovimientos
{
    private readonly InventarioDbContext _contexto;

    public RepositorioMovimientos(InventarioDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task AgregarAsync(MovimientoExistencia movimiento, CancellationToken cancelacion) =>
        await _contexto.Movimientos.AddAsync(movimiento, cancelacion);

    public async Task<IReadOnlyList<MovimientoExistencia>> ListarAsync(
        string sku,
        string? ubicacionId,
        DateTime? desde,
        DateTime? hasta,
        CancellationToken cancelacion)
    {
        var consulta = _contexto.Movimientos
            .AsNoTracking()
            .Where(movimiento => movimiento.Sku == sku);

        if (!string.IsNullOrWhiteSpace(ubicacionId))
        {
            consulta = consulta.Where(movimiento => movimiento.UbicacionId == ubicacionId);
        }

        if (desde is not null)
        {
            var corteInferior = desde.Value.ToUniversalTime();
            consulta = consulta.Where(movimiento => movimiento.RegistradoEn >= corteInferior);
        }

        if (hasta is not null)
        {
            var corteSuperior = hasta.Value.ToUniversalTime();
            consulta = consulta.Where(movimiento => movimiento.RegistradoEn <= corteSuperior);
        }

        return await consulta
            .OrderByDescending(movimiento => movimiento.RegistradoEn)
            .ToListAsync(cancelacion);
    }
}
