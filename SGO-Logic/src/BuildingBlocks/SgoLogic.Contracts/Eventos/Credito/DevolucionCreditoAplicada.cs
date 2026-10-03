namespace SgoLogic.Contracts.Eventos.Credito;

/// <summary>
/// Credito aplico la devolucion de una compra a credito: desconto de la deuda lo que
/// alcanzo y, si el cliente ya habia pagado de mas, pidio el reembolso del excedente.
/// Ordenes la usa para cerrar la devolucion sin esperar ese reembolso. aggregateId =
/// cargoId (id de la cuenta por cobrar).
/// </summary>
public static class DevolucionCreditoAplicada
{
    public const string EventType = "DevolucionCreditoAplicada";
    public const string RoutingKey = "credito.devolucion.aplicada";
    public const string AggregateType = "CuentaPorCobrar";
}

public sealed record DevolucionCreditoAplicadaData(
    string DevolucionId,
    string OrdenOriginalId,
    string ClienteId,
    string Moneda,
    decimal MontoDescontadoDeuda,
    decimal MontoAReembolsar,
    DateTime AplicadaEn);
