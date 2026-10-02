using Microsoft.EntityFrameworkCore;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia;

/// <summary>Implementacion de <see cref="IRepositorioSucursales" /> sobre <see cref="InventarioDbContext" />.</summary>
public sealed class RepositorioSucursales : IRepositorioSucursales
{
    private readonly InventarioDbContext _contexto;

    public RepositorioSucursales(InventarioDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<IReadOnlyList<Sucursal>> ListarActivasAsync(CancellationToken cancelacion) =>
        await _contexto.Sucursales
            .AsNoTracking()
            .Where(sucursal => sucursal.Activa)
            .OrderBy(sucursal => sucursal.TipoUbicacion)
            .ThenBy(sucursal => sucursal.Id)
            .ToListAsync(cancelacion);
}
