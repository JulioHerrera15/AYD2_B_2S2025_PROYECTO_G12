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

/// <summary>
/// Contenido de data del evento ReservaInventarioSolicitada (seccion 7.3 del contrato).
/// <paramref name="CarritoId" /> (opcional, agregado al final) es el carrito del que
/// salio la orden: Inventario adopta sus retenciones al reservar. Null en ordenes que
/// no vienen de un carrito o en mensajes emitidos antes de existir el campo.
/// </summary>
public sealed record ReservaInventarioSolicitadaData(
    string OrdenId,
    MetodoEntrega MetodoEntrega,
    string? UbicacionPreferidaId,
    int TtlReservaSegundos,
    IReadOnlyList<ReservaInventarioLineaData> Lineas,
    DateTime SolicitadaEn,
    string? CarritoId = null);

public sealed record ReservaInventarioLineaData(string Sku, int Cantidad);
