using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Puerto de persistencia de <see cref="Existencia" />. La implementacion vive en
/// Infrastructure/Persistencia y no filtra tipos de EF Core hacia la aplicacion.
/// </summary>
public interface IRepositorioExistencias
{
    /// <summary>Trae todas las existencias (cualquier ubicacion) de los SKU indicados, con seguimiento para escritura.</summary>
    Task<IReadOnlyList<Existencia>> ListarPorSkusAsync(IReadOnlyCollection<string> skus, CancellationToken cancelacion);

    /// <summary>Trae las existencias de un SKU en todas sus ubicaciones, solo lectura.</summary>
    Task<IReadOnlyList<Existencia>> ListarPorSkuAsync(string sku, CancellationToken cancelacion);

    /// <summary>Trae una existencia concreta (SKU + ubicacion) para editarla, o null si no existe.</summary>
    Task<Existencia?> ObtenerAsync(string sku, string ubicacionId, CancellationToken cancelacion);

    /// <summary>Registra una existencia nueva en la unidad de trabajo.</summary>
    Task AgregarAsync(Existencia existencia, CancellationToken cancelacion);
}
