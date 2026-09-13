using Microsoft.EntityFrameworkCore;
using SgoLogic.Contracts.Enumeraciones;
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

    public async Task<(IReadOnlyList<Existencia> Elementos, int Total)> ListarAsync(
        string? ubicacionId,
        TipoUbicacion? tipoUbicacion,
        int pagina,
        int tamano,
        CancellationToken cancelacion)
    {
        var consulta = _contexto.Existencias.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(ubicacionId))
        {
            consulta = consulta.Where(existencia => existencia.UbicacionId == ubicacionId);
        }

        if (tipoUbicacion is not null)
        {
            consulta = consulta.Where(existencia => existencia.TipoUbicacion == tipoUbicacion);
        }

        // El total se cuenta antes de paginar: es cuantas filas hay que recorrer en
        // total, no cuantas trae esta pagina.
        var total = await consulta.CountAsync(cancelacion);

        var elementos = await consulta
            .OrderBy(existencia => existencia.Sku)
            .ThenBy(existencia => existencia.UbicacionId)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync(cancelacion);

        return (elementos, total);
    }

    public async Task<IReadOnlyList<Existencia>> ListarPorUbicacionAsync(
        string ubicacionId,
        CancellationToken cancelacion) =>
        await _contexto.Existencias
            .AsNoTracking()
            .Where(existencia => existencia.UbicacionId == ubicacionId)
            .OrderBy(existencia => existencia.Sku)
            .ToListAsync(cancelacion);

    public async Task<IReadOnlyList<UbicacionResumenDto>> ResumirUbicacionesAsync(CancellationToken cancelacion) =>
        await _contexto.Existencias
            .AsNoTracking()
            .GroupBy(existencia => new { existencia.UbicacionId, existencia.TipoUbicacion })
            .Select(grupo => new UbicacionResumenDto(
                grupo.Key.UbicacionId,
                grupo.Key.TipoUbicacion,
                grupo.Count(),
                grupo.Sum(existencia => existencia.CantidadFisica),
                grupo.Sum(existencia => existencia.CantidadReservada),
                grupo.Sum(existencia => existencia.CantidadFisica - existencia.CantidadReservada)))
            .OrderBy(resumen => resumen.UbicacionId)
            .ToListAsync(cancelacion);

    public async Task AgregarAsync(Existencia existencia, CancellationToken cancelacion) =>
        await _contexto.Existencias.AddAsync(existencia, cancelacion);
}
