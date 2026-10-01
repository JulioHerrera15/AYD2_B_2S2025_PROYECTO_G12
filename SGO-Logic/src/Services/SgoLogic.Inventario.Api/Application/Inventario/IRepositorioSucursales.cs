using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>Puerto de lectura del catalogo de <see cref="Sucursal" />. Solo lectura: el catalogo lo carga la migracion.</summary>
public interface IRepositorioSucursales
{
    /// <summary>Sucursales activas, ordenadas por tipo (centros de distribucion primero) y luego por identificador.</summary>
    Task<IReadOnlyList<Sucursal>> ListarActivasAsync(CancellationToken cancelacion);
}
