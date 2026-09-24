using SgoLogic.Contracts.Enumeraciones;
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

    /// <summary>
    /// Pagina las existencias de todas las ubicaciones (HU-016), ordenadas por SKU y
    /// luego ubicacion. Los filtros son opcionales y se combinan con AND. Devuelve
    /// tambien el total sin paginar.
    /// </summary>
    Task<(IReadOnlyList<Existencia> Elementos, int Total)> ListarAsync(
        string? ubicacionId,
        TipoUbicacion? tipoUbicacion,
        int pagina,
        int tamano,
        CancellationToken cancelacion);

    /// <summary>Trae las existencias de una ubicacion (que hay en esta tienda), solo lectura.</summary>
    Task<IReadOnlyList<Existencia>> ListarPorUbicacionAsync(string ubicacionId, CancellationToken cancelacion);

    /// <summary>
    /// Resume las ubicaciones conocidas agrupando la tabla de existencias. La
    /// agregacion se hace en la base y no en memoria: la vista unificada la consulta
    /// un operador de logistica sobre 34 ubicaciones y no tiene sentido traer cada
    /// fila para contarlas aqui.
    /// </summary>
    Task<IReadOnlyList<UbicacionResumenDto>> ResumirUbicacionesAsync(CancellationToken cancelacion);

    /// <summary>Registra una existencia nueva en la unidad de trabajo.</summary>
    Task AgregarAsync(Existencia existencia, CancellationToken cancelacion);
}
