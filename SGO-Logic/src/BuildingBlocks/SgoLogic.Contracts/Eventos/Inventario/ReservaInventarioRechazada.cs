using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Inventario;

/// <summary>
/// Evento 2 del contrato inventario-eventos-v1. Inventario no pudo reservar lo
/// solicitado: no hay stock suficiente en ninguna ubicacion elegible. No se crea
/// reserva. Lo consume la saga de Ordenes para llevar la orden a RECHAZADA
/// (motivo STOCK_INSUFICIENTE) sin cobrar, ademas de Auditoria.
///
/// Como no hay agregado Reserva, aggregateType es "Reserva" y aggregateId es el
/// ordenId: el mismo transactionId del evento entrante mantiene la correlacion.
/// El conflicto de version optimista NO usa este evento: se resuelve reintentando
/// la entrega del mensaje.
/// </summary>
public static class ReservaInventarioRechazada
{
    public const string EventType = "ReservaInventarioRechazada";
    public const string RoutingKey = "inventario.reserva.rechazada";
    public const string AggregateType = "Reserva";
}

/// <summary>Contenido de data del evento ReservaInventarioRechazada.</summary>
public sealed record ReservaInventarioRechazadaData(
    string OrdenId,
    MotivoRechazoReserva Motivo,
    IReadOnlyList<ReservaRechazadaLineaData> Lineas,
    DateTime RechazadaEn);

public sealed record ReservaRechazadaLineaData(string Sku, int CantidadSolicitada, int CantidadDisponible);
