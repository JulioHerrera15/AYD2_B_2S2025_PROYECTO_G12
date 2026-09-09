using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Ordenes;

/// <summary>
/// Evento 3 del contrato ordenes-eventos-v1. Ordenes solicita a Inventario la
/// reserva con control de concurrencia (CU-01, ADR-005). Se emite al pasar la orden
/// a RESERVA_SOLICITADA.
/// </summary>
public static class ReservaInventarioSolicitada
{
    public const string EventType = "ReservaInventarioSolicitada";
    public const string RoutingKey = "ordenes.reserva.solicitada";
    public const string AggregateType = "Orden";
}

/// <summary>Contenido de data del evento ReservaInventarioSolicitada (seccion 7.3 del contrato).</summary>
public sealed record ReservaInventarioSolicitadaData(
    string OrdenId,
    MetodoEntrega MetodoEntrega,
    string? UbicacionPreferidaId,
    int TtlReservaSegundos,
    IReadOnlyList<ReservaInventarioLineaData> Lineas,
    DateTime SolicitadaEn);

public sealed record ReservaInventarioLineaData(string Sku, int Cantidad);
