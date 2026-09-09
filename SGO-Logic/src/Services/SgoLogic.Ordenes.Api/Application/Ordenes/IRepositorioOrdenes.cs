using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Puerto de persistencia del agregado Orden. La implementacion vive en
/// Infrastructure/Persistencia y no filtra tipos de EF Core hacia la aplicacion.
/// </summary>
public interface IRepositorioOrdenes
{
    /// <summary>Trae la orden con sus lineas y transiciones cargadas.</summary>
    Task<Orden?> ObtenerPorIdAsync(string id, CancellationToken cancelacion);

    /// <summary>Trae la orden por su numero de negocio, con lineas y transiciones.</summary>
    Task<Orden?> ObtenerPorNumeroAsync(string numeroOrden, CancellationToken cancelacion);

    /// <summary>Registra una orden nueva en la unidad de trabajo.</summary>
    Task AgregarAsync(Orden orden, CancellationToken cancelacion);
}
