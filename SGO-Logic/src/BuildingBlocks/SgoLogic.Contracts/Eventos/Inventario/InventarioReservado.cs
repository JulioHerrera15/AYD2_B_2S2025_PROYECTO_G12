using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Inventario;

/// <summary>
/// Evento 1 del contrato inventario-eventos-v1. Inventario confirma la reserva
/// solicitada por Ordenes (CU-01, ADR-005) con control de concurrencia por columna
/// de version y una marca de expiracion (TTL). Lo consume la saga de Ordenes para
/// avanzar a INVENTARIO_RESERVADO, y ademas Auditoria y Consulta de Estado.
///
/// El sobre lo aporta SgoLogic.Contracts.IntegrationEvent y el campo source lo
/// pone el outbox transversal (Outbox:Source = "inventario"): aca solo viaja el
/// contenido de data. aggregateType es "Reserva" y aggregateId es el reservaId.
/// </summary>
public static class InventarioReservado
{
    public const string EventType = "InventarioReservado";
    public const string RoutingKey = "inventario.reserva.confirmada";
    public const string AggregateType = "Reserva";
}

/// <summary>Contenido de data del evento InventarioReservado.</summary>
public sealed record InventarioReservadoData(
    string ReservaId,
    string OrdenId,
    string UbicacionId,
    TipoUbicacion TipoUbicacion,
    int TtlSegundosAplicado,
    IReadOnlyList<ReservaConfirmadaLineaData> Lineas,
    DateTime ReservadaEn,
    DateTime ExpiraEn);

public sealed record ReservaConfirmadaLineaData(string Sku, int Cantidad);
