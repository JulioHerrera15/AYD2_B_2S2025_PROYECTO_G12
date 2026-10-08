namespace SgoLogic.Pagos.Api.Domain;

/// <summary>
/// Que se cobra con un Pago. ORDEN es el cobro de una orden (por defecto, el de
/// siempre); ABONO_CREDITO es el pago de una cuenta por cobrar de Credito, donde
/// Pago.OrdenId lleva la referencia del abono (ABO-...). Las consultas de pagos por
/// orden filtran ORDEN y nunca devuelven abonos.
/// </summary>
public enum ConceptoPago
{
    ORDEN,
    ABONO_CREDITO
}
