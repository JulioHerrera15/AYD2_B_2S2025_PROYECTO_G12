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

    /// <summary>Trae todas las ordenes con alerta fiscal levantada para el panel de incidencias.</summary>
    Task<IReadOnlyList<Orden>> ObtenerConAlertaFiscalAsync(CancellationToken cancelacion);

    /// <summary>Trae todas las devoluciones de una orden, con sus lineas.</summary>
    Task<IReadOnlyList<Devolucion>> ObtenerDevolucionesPorOrdenAsync(string ordenId, CancellationToken cancelacion);

    /// <summary>Trae una devolucion por su identificador, con sus lineas.</summary>
    Task<Devolucion?> ObtenerDevolucionPorIdAsync(string devolucionId, CancellationToken cancelacion);

    /// <summary>Registra una devolucion en la unidad de trabajo.</summary>
    Task AgregarDevolucionAsync(Devolucion devolucion, CancellationToken cancelacion);

    /// <summary>
    /// Ids de las ordenes en EVALUACION_CREDITO_SOLICITADA cuya ultima transicion es
    /// anterior o igual a <paramref name="limite" />, las mas viejas primero. Solo
    /// lectura: el timeout carga cada orden por separado antes de moverla.
    /// </summary>
    Task<IReadOnlyList<string>> ListarEvaluacionesCreditoVencidasAsync(
        DateTime limite,
        int maximo,
        CancellationToken cancelacion);
}
