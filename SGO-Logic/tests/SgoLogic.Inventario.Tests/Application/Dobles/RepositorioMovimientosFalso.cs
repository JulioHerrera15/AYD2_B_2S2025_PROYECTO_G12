using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Tests.Application.Dobles;

/// <summary>Doble en memoria de <see cref="IRepositorioMovimientos" />.</summary>
public sealed class RepositorioMovimientosFalso : IRepositorioMovimientos
{
    private readonly List<MovimientoExistencia> _movimientos = new();

    public IReadOnlyList<MovimientoExistencia> Todos => _movimientos;

    public Task AgregarAsync(MovimientoExistencia movimiento, CancellationToken cancelacion)
    {
        _movimientos.Add(movimiento);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MovimientoExistencia>> ListarAsync(
        string sku,
        string? ubicacionId,
        DateTime? desde,
        DateTime? hasta,
        CancellationToken cancelacion) =>
        Task.FromResult<IReadOnlyList<MovimientoExistencia>>(
            _movimientos
                // Se invierte antes de ordenar porque OrderByDescending es estable:
                // dos asientos registrados en el mismo tick deben salir con el ultimo
                // primero, igual que los desempata la base por orden de insercion.
                .AsEnumerable()
                .Reverse()
                .Where(movimiento => movimiento.Sku == sku)
                .Where(movimiento => ubicacionId is null || movimiento.UbicacionId == ubicacionId)
                .Where(movimiento => desde is null || movimiento.RegistradoEn >= desde.Value.ToUniversalTime())
                .Where(movimiento => hasta is null || movimiento.RegistradoEn <= hasta.Value.ToUniversalTime())
                .OrderByDescending(movimiento => movimiento.RegistradoEn)
                .ToList());
}
