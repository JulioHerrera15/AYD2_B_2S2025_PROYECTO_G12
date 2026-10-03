namespace SgoLogic.Contracts.Eventos.Credito;

/// <summary>
/// Credito pide a Pagos devolver a la tarjeta (total o parte de) lo cobrado en un
/// abono: la cuenta se anulo con abonos aplicados, el cobro llego con la cuenta ya
/// anulada o una devolucion supero el saldo pendiente. Pagos es idempotente por
/// reembolsoId y nunca reembolsa mas de lo cobrado en el abono. aggregateId = abonoId.
/// </summary>
public static class ReembolsoAbonoCreditoSolicitado
{
    public const string EventType = "ReembolsoAbonoCreditoSolicitado";
    public const string RoutingKey = "credito.abono.reembolso-solicitado";
    public const string AggregateType = "AbonoCredito";
}

/// <summary>Valores de <see cref="ReembolsoAbonoCreditoSolicitadoData.Motivo" />.</summary>
public static class MotivosReembolsoAbono
{
    public const string Cancelacion = "CANCELACION";
    public const string Devolucion = "DEVOLUCION";
}

/// <param name="Referencia">Referencia del abono (ABO-...); Pagos la usa para encontrar el cobro.</param>
/// <param name="Motivo">CANCELACION o DEVOLUCION (<see cref="MotivosReembolsoAbono" />).</param>
public sealed record ReembolsoAbonoCreditoSolicitadoData(
    string ReembolsoId,
    string AbonoId,
    string Referencia,
    string ClienteId,
    string Moneda,
    decimal Monto,
    string Motivo,
    DateTime SolicitadoEn);
