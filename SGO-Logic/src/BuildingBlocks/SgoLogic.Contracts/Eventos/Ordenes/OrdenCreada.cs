using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Ordenes;

/// <summary>
/// Evento 1 del contrato ordenes-eventos-v1. Se emite al persistir la orden, antes
/// de solicitar la reserva. Siempre con sequence 1. El sobre lo aporta
/// SgoLogic.Contracts.IntegrationEvent y el campo source lo pone el outbox
/// transversal (Outbox:Source = "ordenes"): aca solo viaja el contenido de data.
/// </summary>
public static class OrdenCreada
{
    public const string EventType = "OrdenCreada";
    public const string RoutingKey = "ordenes.orden.creada";
    public const string AggregateType = "Orden";
}

/// <summary>Contenido de data del evento OrdenCreada (seccion 7.1 del contrato).</summary>
public sealed record OrdenCreadaData(
    string OrdenId,
    string NumeroOrden,
    string ClienteId,
    EstadoOrden Estado,
    Canal Canal,
    MetodoEntrega MetodoEntrega,
    string? UbicacionSolicitadaId,
    Moneda Moneda,
    decimal Subtotal,
    decimal Impuestos,
    decimal Total,
    IReadOnlyList<OrdenCreadaLineaData> Lineas,
    DireccionEntregaData? DireccionEntrega,
    DateTime CreadaEn);

public sealed record OrdenCreadaLineaData(
    string Sku,
    string Descripcion,
    int Cantidad,
    decimal PrecioUnitario,
    decimal Subtotal);
