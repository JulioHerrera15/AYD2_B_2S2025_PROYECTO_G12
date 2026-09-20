using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Despacho.Api.Domain;

namespace SgoLogic.Despacho.Api.Application.Despacho;

/// <summary>Puerto de persistencia del agregado <see cref="Domain.Despacho" />.</summary>
public interface IRepositorioDespachos
{
    Task<Domain.Despacho?> ObtenerPorOrdenAsync(string ordenId, CancellationToken cancelacion);

    Task<IReadOnlyList<Domain.Despacho>> ListarPorUbicacionOrigenAsync(
        string ubicacionOrigenId,
        EstadoDespacho? estado,
        CancellationToken cancelacion);

    Task AgregarAsync(Domain.Despacho despacho, CancellationToken cancelacion);
}

/// <summary>
/// Confirma en una sola transaccion el cambio de negocio y las filas de outbox.
/// Traduce el conflicto de bloqueo optimista a <see cref="ExcepcionConflictoConcurrencia" />.
/// </summary>
public interface IUnidadDeTrabajo
{
    Task GuardarCambiosAsync(CancellationToken cancelacion);
}

/// <summary>Correlativo para el id <c>DES-anio-000000</c>.</summary>
public interface IGeneradorCorrelativoDespacho
{
    Task<long> SiguienteAsync(CancellationToken cancelacion);
}

/// <summary>
/// Pregunta a Inventario desde que ubicacion se reservo realmente. Es una llamada
/// entre servicios propios, no a un proveedor externo: si Inventario no responde,
/// Despacho no tiene alternativa y debe fallar el mensaje para que la cola lo
/// reintente. Por eso no lleva circuit breaker.
/// </summary>
public interface IClienteInventario
{
    Task<UbicacionReservaDto?> ObtenerUbicacionDeReservaAsync(string ordenId, CancellationToken cancelacion);
}

public sealed record UbicacionReservaDto(string UbicacionId, TipoUbicacion TipoUbicacion);

/// <summary>
/// Pide la guia a LogiExpress. A diferencia de Inventario, aqui el fallo SI es un
/// resultado de negocio valido: un timeout deja el despacho en guia pendiente y el
/// pedido sale igual.
/// </summary>
public interface IClienteLogiExpress
{
    Task<GuiaEmitidaDto?> SolicitarGuiaAsync(SolicitudGuia solicitud, CancellationToken cancelacion);

    /// <summary>Anula una guia ya emitida. Devuelve false si el proveedor no la acepto.</summary>
    Task<bool> AnularGuiaAsync(string guiaId, CancellationToken cancelacion);
}

public sealed record SolicitudGuia(
    string OrdenId,
    string UbicacionOrigenId,
    string DireccionEntrega,
    IReadOnlyList<LineaGuiaDto> Lineas);

public sealed record LineaGuiaDto(string Sku, int Cantidad, string? Descripcion);

public sealed record GuiaEmitidaDto(string GuiaId, string TrackingUrl, string Transportista);
