using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Ordenes;

/// <summary>
/// Evento 8 del contrato ordenes-eventos-v1. La venta quedo firme. Alimenta
/// Integracion POS (CU-09) e Integracion ERP (CU-13). Se emite al pasar la orden a
/// CONFIRMADA_PENDIENTE_FISCAL, no al certificar: el folio definitivo llega despues
/// en el evento 12. Prohibido como fuente de estado (seccion 6 del contrato).
/// documentoFiscalId es siempre null en este evento.
/// </summary>
public static class OrdenConfirmada
{
    public const string EventType = "OrdenConfirmada";
    public const string RoutingKey = "ordenes.orden.confirmada";
    public const string AggregateType = "Orden";
}

/// <summary>Contenido de data del evento OrdenConfirmada (seccion 7.8 del contrato).</summary>
public sealed record OrdenConfirmadaData(
    string OrdenId,
    string NumeroOrden,
    string ClienteId,
    Canal Canal,
    string? TiendaDestinoId,
    Moneda Moneda,
    decimal Subtotal,
    decimal Impuestos,
    decimal Total,
    string PagoId,
    string? DocumentoFiscalId,
    IReadOnlyList<OrdenConfirmadaLineaData> Lineas,
    DateTime ConfirmadaEn);

public sealed record OrdenConfirmadaLineaData(
    string Sku,
    string Descripcion,
    int Cantidad,
    decimal PrecioUnitario,
    decimal Subtotal);
