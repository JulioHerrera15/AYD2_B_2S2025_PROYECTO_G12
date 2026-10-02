namespace SgoLogic.Contracts.Eventos.Credito;

/// <summary>
/// Credito anulo la cuenta por cobrar de una orden cancelada y devolvio el monto al
/// disponible de la linea. Hoy solo lo registra Auditoria. aggregateId = ordenId.
/// </summary>
public static class CargoCreditoAnulado
{
    public const string EventType = "CargoCreditoAnulado";
    public const string RoutingKey = "credito.cargo.anulado";
    public const string AggregateType = "CuentaPorCobrar";
}

public sealed record CargoCreditoAnuladoData(
    string OrdenId,
    string ClienteId,
    string CargoId,
    decimal Monto,
    string Moneda,
    string Motivo,
    DateTime AnuladoEn);
