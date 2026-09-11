using Microsoft.EntityFrameworkCore;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia;

/// <summary>
/// Implementacion de <see cref="IRepositorioExistencias" /> sobre
/// <see cref="InventarioDbContext" />. No llama a SaveChanges: eso es tarea de la
/// unidad de trabajo.
/// </summary>
public sealed class RepositorioExistencias : IRepositorioExistencias
{
    private readonly InventarioDbContext _contexto;

    public RepositorioExistencias(InventarioDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<IReadOnlyList<Existencia>> ListarPorSkusAsync(
        IReadOnlyCollection<string> skus,
        CancellationToken cancelacion)
    {
        if (skus.Count == 0)
        {
            return Array.Empty<Existencia>();
        }

        return await _contexto.Existencias
            .Where(existencia => skus.Contains(existencia.Sku))
            .ToListAsync(cancelacion);
    }

    public async Task<IReadOnlyList<Existencia>> ListarPorSkuAsync(string sku, CancellationToken cancelacion) =>
        await _contexto.Existencias
            .AsNoTracking()
            .Where(existencia => existencia.Sku == sku)
            .OrderBy(existencia => existencia.UbicacionId)
            .ToListAsync(cancelacion);

    public Task<Existencia?> ObtenerAsync(string sku, string ubicacionId, CancellationToken cancelacion) =>
        _contexto.Existencias
            .FirstOrDefaultAsync(
                existencia => existencia.Sku == sku && existencia.UbicacionId == ubicacionId,
                cancelacion);

    public async Task AgregarAsync(Existencia existencia, CancellationToken cancelacion) =>
        await _contexto.Existencias.AddAsync(existencia, cancelacion);
}
