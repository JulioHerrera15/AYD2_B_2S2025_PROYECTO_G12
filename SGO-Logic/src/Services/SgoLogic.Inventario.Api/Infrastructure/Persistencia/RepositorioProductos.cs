using Microsoft.EntityFrameworkCore;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia;

/// <summary>Implementacion de <see cref="IRepositorioProductos" /> sobre <see cref="InventarioDbContext" />.</summary>
public sealed class RepositorioProductos : IRepositorioProductos
{
    private readonly InventarioDbContext _contexto;

    public RepositorioProductos(InventarioDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<IReadOnlyList<Producto>> ListarActivosAsync(CancellationToken cancelacion) =>
        await _contexto.Productos
            .AsNoTracking()
            .Where(producto => producto.Activo)
            .OrderBy(producto => producto.Sku)
            .ToListAsync(cancelacion);

    public Task<Producto?> ObtenerAsync(string sku, CancellationToken cancelacion) =>
        _contexto.Productos.FirstOrDefaultAsync(producto => producto.Sku == sku, cancelacion);

    public async Task AgregarAsync(Producto producto, CancellationToken cancelacion) =>
        await _contexto.Productos.AddAsync(producto, cancelacion);

    public Task GuardarCambiosAsync(CancellationToken cancelacion) => _contexto.SaveChangesAsync(cancelacion);
}
