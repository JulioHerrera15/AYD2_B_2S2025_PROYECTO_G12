namespace SgoLogic.Contracts.Eventos.Credito;

/// <summary>
/// Credito pide a Pagos cobrar con tarjeta un abono a una cuenta por cobrar. Un
/// reintento reemite este evento con el MISMO abonoId y el intentoNumero siguiente:
/// Pagos usa el mismo pago y la misma clave de idempotencia hacia el proveedor, asi
/// que nunca hay cobro doble. aggregateId = abonoId.
/// </summary>
public static class CobroAbonoCreditoSolicitado
{
    public const string EventType = "CobroAbonoCreditoSolicitado";
    public const string RoutingKey = "credito.abono.cobro-solicitado";
    public const string AggregateType = "AbonoCredito";
}

/// <param name="Referencia">ABO-{yyyy}-{correlativo de 6 digitos}; Pagos la usa en lugar del ordenId.</param>
/// <param name="TokenMedioPago">Token de la boveda de Pagos; debe ser del mismo cliente. Nunca PAN ni CVV.</param>
public sealed record CobroAbonoCreditoSolicitadoData(
    string AbonoId,
    string Referencia,
    string ClienteId,
    string Moneda,
    decimal Monto,
    string TokenMedioPago,
    int IntentoNumero,
    DateTime SolicitadoEn);
