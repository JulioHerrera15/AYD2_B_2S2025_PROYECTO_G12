namespace SgoLogic.Contracts.Eventos.Inventario;

/// <summary>
/// Evento 3 del contrato inventario-eventos-v1. Inventario liberó por su cuenta una
/// reserva cuyo TTL venció (HU-002, EV-23): las unidades volvieron al disponible sin
/// intervención manual. Lo consume Órdenes para cancelar la orden asociada con motivo
/// RESERVA_VENCIDA, y además Auditoría y Consulta de Estado.
///
/// El sobre lo aporta SgoLogic.Contracts.IntegrationEvent; aca solo viaja el
/// contenido de data. aggregateType es "Reserva" y aggregateId es el reservaId, igual
/// que en InventarioReservado, de modo que sequence sigue siendo monotonica por
/// reserva (1 al confirmarse, 2 al expirar).
/// </summary>
public static class ReservaInventarioExpirada
{
    public const string EventType = "ReservaInventarioExpirada";
    public const string RoutingKey = "inventario.reserva.expirada";
    public const string AggregateType = "Reserva";
}

/// <summary>Contenido de data del evento ReservaInventarioExpirada.</summary>
public sealed record ReservaInventarioExpiradaData(
    string ReservaId,
    string OrdenId,
    string UbicacionId,
    IReadOnlyList<ReservaConfirmadaLineaData> Lineas,
    DateTime ExpiraEn,
    DateTime ExpiradaEn);
